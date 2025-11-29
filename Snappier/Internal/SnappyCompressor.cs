using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snappier.Internal;

internal class SnappyCompressor : IDisposable
{
    private HashTable? _workingMemory = new();

    [Obsolete("Retained for benchmark comparisons to previous versions")]
    [ExcludeFromCodeCoverage]
    public int Compress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!TryCompress(input, output, out int bytesWritten))
        {
            ThrowHelper.ThrowArgumentExceptionInsufficientOutputBuffer(nameof(output));
        }

        return bytesWritten;
    }

    public bool TryCompress(ReadOnlySpan<byte> input, Span<byte> output, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(_workingMemory is null, this);
        if (input.Overlaps(output))
        {
            ThrowHelper.ThrowInvalidOperationException("Input and output spans must not overlap.");
        }

        _workingMemory.EnsureCapacity(input.Length);

        if (!VarIntEncoding.TryWrite(output, (uint)input.Length, out bytesWritten))
        {
            return false;
        }
        output = output.Slice(bytesWritten);

        while (input.Length > 0)
        {
            ReadOnlySpan<byte> fragment = input.Slice(0, Math.Min(input.Length, (int)Constants.BlockSize));

            Span<ushort> hashTable = _workingMemory.GetHashTable(fragment.Length);

            int maxOutput = Helpers.MaxCompressedLength(fragment.Length);

            int written;
            if (output.Length >= maxOutput)
            {
                // The output span is large enough to hold the maximum possible compressed output,
                // compress directly to that span.

                written = CompressFragment(fragment, output, hashTable);
            }
            else
            {
                // The output span is too small to hold the maximum possible compressed output,
                // compress to a temporary buffer and copy the compressed data to the output span.

                byte[] scratch = ArrayPool<byte>.Shared.Rent(maxOutput);
                written = CompressFragment(fragment, scratch.AsSpan(), hashTable);
                if (output.Length < written)
                {
                    Helpers.ClearAndReturn(scratch, written);
                    bytesWritten = 0;
                    return false;
                }

                Span<byte> writtenScratch = scratch.AsSpan(0, written);
                writtenScratch.CopyTo(output);
                writtenScratch.Clear();
                ArrayPool<byte>.Shared.Return(scratch);
            }

            output = output.Slice(written);
            bytesWritten += written;

            input = input.Slice(fragment.Length);
        }

        return true;
    }

    public void Compress(ReadOnlySequence<byte> input, IBufferWriter<byte> bufferWriter)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        if (input.Length > uint.MaxValue)
        {
            ThrowHelper.ThrowArgumentException($"{nameof(input)} is larger than the maximum size of {uint.MaxValue} bytes.", nameof(input));
        }
        ObjectDisposedException.ThrowIf(_workingMemory is null, this);

        _workingMemory.EnsureCapacity(input.Length);

        Span<byte> sizeBuffer = bufferWriter.GetSpan(VarIntEncoding.MaxLength);
        if (!VarIntEncoding.TryWrite(sizeBuffer, (uint)input.Length, out int bytesWritten))
        {
            ThrowHelper.ThrowInvalidOperationException("IBufferWriter<byte> did not return a sufficient span for length prefix.");
        }
        bufferWriter.Advance(bytesWritten);

        while (input.Length > 0)
        {
            SequencePosition position = input.GetPosition(Math.Min(input.Length, Constants.BlockSize));
            ReadOnlySequence<byte> fragment = input.Slice(0, position);

            if (fragment.IsSingleSegment || fragment.First.Length >= (Constants.BlockSize / 2))
            {
                // Either this fragment is contiguous, or the first segment in the fragment is at least 32KB.
                // In either case, compress the first (and possibly only) segment.

#if NET8_0_OR_GREATER
                ReadOnlySpan<byte> fragmentSpan = fragment.FirstSpan;
#else
                ReadOnlySpan<byte> fragmentSpan = fragment.First.Span;
#endif

                CompressFragment(fragmentSpan, bufferWriter);

                // Advance the length of the processed segment of the fragment
                input = input.Slice(fragmentSpan.Length);
            }
            else
            {
                // This fragment is split and the first segment is <32KB, copy the entire fragment to a single
                // buffer before compressing.

                int fragmentLength = (int)fragment.Length;
                byte[] scratch = ArrayPool<byte>.Shared.Rent(fragmentLength);

                Span<byte> usedScratch = scratch.AsSpan(0, fragmentLength);
                fragment.CopyTo(usedScratch);

                CompressFragment(usedScratch, bufferWriter);

                usedScratch.Clear();
                ArrayPool<byte>.Shared.Return(scratch);

                // Advance the length of the entire fragment
                input = input.Slice(position);
            }
        }
    }

    public void Dispose()
    {
        _workingMemory?.Dispose();
        _workingMemory = null;
    }

    #region CompressFragment

    private void CompressFragment(ReadOnlySpan<byte> fragment, IBufferWriter<byte> bufferWriter)
    {
        DebugExtensions.Assert(_workingMemory is not null);

        Span<ushort> hashTable = _workingMemory.GetHashTable(fragment.Length);

        int maxOutput = Helpers.MaxCompressedLength(fragment.Length);

        Span<byte> fragmentBuffer = bufferWriter.GetSpan(maxOutput);

        // Validate proper implementation of bufferWriter to prevent buffer overflows
        if (fragmentBuffer.Length < maxOutput)
        {
            ThrowHelper.ThrowInvalidOperationException("IBufferWriter<byte> did not return a sufficient span");
        }

        int bytesWritten = CompressFragment(fragment, fragmentBuffer, hashTable);
        bufferWriter.Advance(bytesWritten);
    }

    private static int CompressFragment(ReadOnlySpan<byte> input, Span<byte> output, Span<ushort> tableSpan)
    {
        unchecked
        {
            DebugExtensions.Assert(input.Length <= Constants.BlockSize);
            DebugExtensions.Assert((tableSpan.Length & (tableSpan.Length - 1)) == 0); // table must be power of two

            uint mask = (uint)(2 * (tableSpan.Length - 1));

            ref readonly byte inputStart = ref input[0];
            ref readonly byte inputEnd = ref Unsafe.Add(in inputStart, input.Length);
            ref readonly byte ip = ref inputStart;

            ref byte op = ref output[0];
            ref ushort table = ref tableSpan[0];

            if (input.Length >= Constants.InputMarginBytes)
            {
                ref readonly byte ipLimit = ref Unsafe.Subtract(in inputEnd, Constants.InputMarginBytes);

                for (uint preload = Helpers.UnsafeReadUInt32(in Unsafe.Add(in ip, 1));;)
                {
                    // Bytes in [nextEmit, ip) will be emitted as literal bytes.  Or
                    // [nextEmit, ipEnd) after the main loop.
                    ref readonly byte nextEmit = ref ip;
                    ip = ref Unsafe.Add(in ip, 1);
                    ulong data = Helpers.UnsafeReadUInt64(in ip);

                    // The body of this loop calls EmitLiteral once and then EmitCopy one or
                    // more times.  (The exception is that when we're close to exhausting
                    // the input we goto emit_remainder.)
                    //
                    // In the first iteration of this loop we're just starting, so
                    // there's nothing to copy, so calling EmitLiteral once is
                    // necessary.  And we only start a new iteration when the
                    // current iteration has determined that a call to EmitLiteral will
                    // precede the next call to EmitCopy (if any).
                    //
                    // Step 1: Scan forward in the input looking for a 4-byte-long match.
                    // If we get close to exhausting the input then goto emit_remainder.
                    //
                    // Heuristic match skipping: If 32 bytes are scanned with no matches
                    // found, start looking only at every other byte. If 32 more bytes are
                    // scanned (or skipped), look at every third byte, etc.. When a match is
                    // found, immediately go back to looking at every byte. This is a small
                    // loss (~5% performance, ~0.1% density) for compressible data due to more
                    // bookkeeping, but for non-compressible data (such as JPEG) it's a huge
                    // win since the compressor quickly "realizes" the data is incompressible
                    // and doesn't bother looking for matches everywhere.
                    //
                    // The "skip" variable keeps track of how many bytes there are since the
                    // last match; dividing it by 32 (ie. right-shifting by five) gives the
                    // number of bytes to move ahead for each iteration.
                    int skip = 32;

                    scoped ref readonly byte candidate = ref Unsafe.NullRef<byte>();
                    if (Unsafe.ByteOffset(in ip, in ipLimit) >= (nint) 16)
                    {
                        nint delta = Unsafe.ByteOffset(in inputStart, in ip);
                        for (int j = 0; j < 16; j += 4)
                        {
                            // Manually unroll this loop into chunks of 4

                            uint dword = j == 0 ? preload : (uint) data;
                            DebugExtensions.Assert(dword == Helpers.UnsafeReadUInt32(in Unsafe.Add(in ip, j)));
                            ref ushort tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                            candidate = ref Unsafe.Add(in inputStart, tableEntry);
                            DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in candidate, in inputStart));
                            DebugExtensions.Assert(Unsafe.IsAddressLessThan(in candidate, in Unsafe.Add(in ip, j)));
                            tableEntry = (ushort) (delta + j);

                            if (Helpers.UnsafeReadUInt32(in candidate) == dword)
                            {
                                op = (byte) (Constants.Literal | (j << 2));
                                CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op,  1));
                                ip = ref Unsafe.Add(in ip, j);
                                op = ref Unsafe.Add(ref op, j + 2);
                                goto emit_match;
                            }

                            int i1 = j + 1;
                            dword = (uint)(data >> 8);
                            DebugExtensions.Assert(dword == Helpers.UnsafeReadUInt32(in Unsafe.Add(in ip, i1)));
                            tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                            candidate = ref Unsafe.Add(in inputStart, tableEntry);
                            DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in candidate, in inputStart));
                            DebugExtensions.Assert(Unsafe.IsAddressLessThan(in candidate, in Unsafe.Add(in ip, i1)));
                            tableEntry = (ushort) (delta + i1);

                            if (Helpers.UnsafeReadUInt32(in candidate) == dword)
                            {
                                op = (byte) (Constants.Literal | (i1 << 2));
                                CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                ip = ref Unsafe.Add(in ip, i1);
                                op = ref Unsafe.Add(ref op, i1 + 2);
                                goto emit_match;
                            }

                            int i2 = j + 2;
                            dword = (uint)(data >> 16);
                            DebugExtensions.Assert(dword == Helpers.UnsafeReadUInt32(in Unsafe.Add(in ip, i2)));
                            tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                            candidate = ref Unsafe.Add(in inputStart, tableEntry);
                            DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in candidate, in inputStart));
                            DebugExtensions.Assert(Unsafe.IsAddressLessThan(in candidate, in Unsafe.Add(in ip, i2)));
                            tableEntry = (ushort) (delta + i2);

                            if (Helpers.UnsafeReadUInt32(in candidate) == dword)
                            {
                                op = (byte) (Constants.Literal | (i2 << 2));
                                CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                ip = ref Unsafe.Add(in ip, i2);
                                op = ref Unsafe.Add(ref op, i2 + 2);
                                goto emit_match;
                            }

                            int i3 = j + 3;
                            dword = (uint)(data >> 24);
                            DebugExtensions.Assert(dword == Helpers.UnsafeReadUInt32(in Unsafe.Add(in ip, i3)));
                            tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                            candidate = ref Unsafe.Add(in inputStart, tableEntry);
                            DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in candidate, in inputStart));
                            DebugExtensions.Assert(Unsafe.IsAddressLessThan(in candidate, in Unsafe.Add(in ip, i3)));
                            tableEntry = (ushort) (delta + i3);

                            if (Helpers.UnsafeReadUInt32(in candidate) == dword)
                            {
                                op = (byte) (Constants.Literal | (i3 << 2));
                                CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                ip = ref Unsafe.Add(in ip, i3);
                                op = ref Unsafe.Add(ref op, i3 + 2);
                                goto emit_match;
                            }

                            data = Helpers.UnsafeReadUInt64(in Unsafe.Add(in ip, j + 4));
                        }

                        ip = ref Unsafe.Add(in ip, 16);
                        skip += 16;
                    }

                    while (true)
                    {
                        DebugExtensions.Assert((uint) data == Helpers.UnsafeReadUInt32(in ip));
                        ref ushort tableEntry = ref HashTable.TableEntry(ref table, (uint) data, mask);
                        int bytesBetweenHashLookups = skip >> 5;
                        skip += bytesBetweenHashLookups;

                        ref readonly byte nextIp = ref Unsafe.Add(in ip, bytesBetweenHashLookups);
                        if (Unsafe.IsAddressGreaterThan(in nextIp, in ipLimit))
                        {
                            ip = ref nextEmit;
                            goto emit_remainder;
                        }

                        candidate = ref Unsafe.Add(in inputStart, tableEntry);
                        DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in candidate, in inputStart));
                        DebugExtensions.Assert(Unsafe.IsAddressLessThan(in candidate, in ip));

                        tableEntry = (ushort)Unsafe.ByteOffset(in inputStart, in ip);
                        if ((uint) data == Helpers.UnsafeReadUInt32(in candidate))
                        {
                            break;
                        }

                        data = Helpers.UnsafeReadUInt32(in nextIp);
                        ip = ref nextIp;
                    }

                    // Step 2: A 4-byte match has been found.  We'll later see if more
                    // than 4 bytes match.  But, prior to the match, input
                    // bytes [next_emit, ip) are unmatched.  Emit them as "literal bytes."
                    DebugExtensions.Assert(!Unsafe.IsAddressGreaterThan(in Unsafe.Add(in nextEmit, 16), in inputEnd));
                    op = ref EmitLiteralFast(ref op, in nextEmit, (uint)Unsafe.ByteOffset(in nextEmit, in ip));

                    // Step 3: Call EmitCopy, and then see if another EmitCopy could
                    // be our next move.  Repeat until we find no match for the
                    // input immediately after what was consumed by the last EmitCopy call.
                    //
                    // If we exit this loop normally then we need to call EmitLiteral next,
                    // though we don't yet know how big the literal will be.  We handle that
                    // by proceeding to the next iteration of the main loop.  We also can exit
                    // this loop via goto if we get close to exhausting the input.

                    emit_match:
                    do
                    {
                        // We have a 4-byte match at ip, and no need to emit any
                        // "literal bytes" prior to ip.
                        ref readonly byte emitBase = ref ip;

                        (int matchLength, bool matchLengthLessThan8) =
                            FindMatchLength(in Unsafe.Add(in candidate, 4), in Unsafe.Add(in ip, 4), in inputEnd, ref data);

                        int matched = 4 + matchLength;
                        ip = ref Unsafe.Add(in ip, matched);

                        nint offset = Unsafe.ByteOffset(in candidate, in emitBase);
                        if (matchLengthLessThan8)
                        {
                            op = ref EmitCopyLenLessThan12(ref op, offset, matched);
                        }
                        else
                        {
                            op = ref EmitCopyLenGreaterThanOrEqualTo12(ref op, offset, matched);
                        }

                        if (!Unsafe.IsAddressLessThan(in ip, in ipLimit))
                        {
                            goto emit_remainder;
                        }

                        // Expect 5 bytes to match
                        DebugExtensions.Assert((data & 0xfffffffffful) ==
                                     (Helpers.UnsafeReadUInt64(in ip) & 0xfffffffffful));

                        // We are now looking for a 4-byte match again.  We read
                        // table[Hash(ip, mask)] for that.  To improve compression,
                        // we also update table[Hash(ip - 1, mask)] and table[Hash(ip, mask)].
                        HashTable.TableEntry(ref table, Helpers.UnsafeReadUInt32(in Unsafe.Subtract(in ip, 1)), mask) =
                            (ushort) (Unsafe.ByteOffset(in inputStart, in ip) - 1);
                        ref ushort tableEntry = ref HashTable.TableEntry(ref table, (uint) data, mask);
                        candidate = ref Unsafe.Add(in inputStart, tableEntry);
                        tableEntry = (ushort)Unsafe.ByteOffset(in inputStart, in ip);
                    } while ((uint) data == Helpers.UnsafeReadUInt32(in candidate));

                    // Because the least significant 5 bytes matched, we can utilize data
                    // for the next iteration.
                    preload = (uint) (data >> 8);
                }
            }

            emit_remainder:
            // Emit the remaining bytes as a literal
            if (Unsafe.IsAddressLessThan(in ip, in inputEnd))
            {
                op = ref EmitLiteralSlow(ref op, in ip, (uint) Unsafe.ByteOffset(in ip, in inputEnd));
            }

            return (int) Unsafe.ByteOffset(ref output[0], ref op);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitLiteralFast(ref byte op, ref readonly byte literal, uint length)
    {
        DebugExtensions.Assert(length > 0);

        if (length <= 16)
        {
            uint n = length - 1;
            op = unchecked((byte)(Constants.Literal | (n << 2)));
            op = ref Unsafe.Add(ref op, 1);

            CopyHelpers.UnalignedCopy128(in literal, ref op);
            return ref Unsafe.Add(ref op, length);
        }

        return ref EmitLiteralSlow(ref op, in literal, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitLiteralSlow(ref byte op, ref readonly byte literal, uint length)
    {
        uint n = length - 1;
        if (n < 60)
        {
            op = unchecked((byte) (Constants.Literal | (n << 2)));
            op = ref Unsafe.Add(ref op, 1);
        }
        else
        {
            DebugExtensions.Assert(n > 0);
            int count = (Helpers.Log2Floor(n) >> 3) + 1;

            DebugExtensions.Assert(count >= 1);
            DebugExtensions.Assert(count <= 4);
            op = unchecked((byte)(Constants.Literal | ((59 + count) << 2)));
            op = ref Unsafe.Add(ref op, 1);

            // Encode in upcoming bytes.
            // Write 4 bytes, though we may care about only 1 of them. The output buffer
            // is guaranteed to have at least 3 more spaces left as 'len >= 61' holds
            // here and there is a std::memcpy() of size 'len' below.
            Helpers.UnsafeWriteUInt32(ref op, n);
            op = ref Unsafe.Add(ref op, count);
        }

        Unsafe.CopyBlockUnaligned(ref op, in literal, length);
        return ref Unsafe.Add(ref op,  length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitCopyAtMost64LenLessThan12(ref byte op, long offset, long length)
    {
        DebugExtensions.Assert(length <= 64);
        DebugExtensions.Assert(length >= 4);
        DebugExtensions.Assert(offset < 65536);
        DebugExtensions.Assert(length < 12);

        unchecked
        {
            uint u = (uint) ((length << 2) + (offset << 8));
            uint copy1 = (uint) (Constants.Copy1ByteOffset - (4 << 2) + ((offset >> 3) & 0xe0));
            uint copy2 = (uint) (Constants.Copy2ByteOffset - (1 << 2));

            // It turns out that offset < 2048 is a difficult to predict branch.
            // `perf record` shows this is the highest percentage of branch misses in
            // benchmarks. This code produces branch free code, the data dependency
            // chain that bottlenecks the throughput is so long that a few extra
            // instructions are completely free (IPC << 6 because of data deps).
            u += offset < 2048 ? copy1 : copy2;
            Helpers.UnsafeWriteUInt32(ref op, u);
        }

        return ref Unsafe.Add(ref op, offset < 2048 ? 2 : 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitCopyAtMost64LenGreaterThanOrEqualTo12(ref byte op, long offset, long length)
    {
        DebugExtensions.Assert(length <= 64);
        DebugExtensions.Assert(length >= 4);
        DebugExtensions.Assert(offset < 65536);
        DebugExtensions.Assert(length >= 12);

        // Write 4 bytes, though we only care about 3 of them.  The output buffer
        // is required to have some slack, so the extra byte won't overrun it.
        uint u = unchecked((uint)(Constants.Copy2ByteOffset + ((length - 1) << 2) + (offset << 8)));
        Helpers.UnsafeWriteUInt32(ref op, u);
        return ref Unsafe.Add(ref op, 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitCopyLenLessThan12(ref byte op, long offset, long length)
    {
        DebugExtensions.Assert(length < 12);

        return ref EmitCopyAtMost64LenLessThan12(ref op, offset, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte EmitCopyLenGreaterThanOrEqualTo12(ref byte op, long offset, long length)
    {
        DebugExtensions.Assert(length >= 12);

        // A special case for len <= 64 might help, but so far measurements suggest
        // it's in the noise.

        // Emit 64 byte copies but make sure to keep at least four bytes reserved.
        while (length >= 68)
        {
            op = ref EmitCopyAtMost64LenGreaterThanOrEqualTo12(ref op, offset, 64);
            length -= 64;
        }

        // One or two copies will now finish the job.
        if (length > 64) {
            op = ref EmitCopyAtMost64LenGreaterThanOrEqualTo12(ref op, offset, 60);
            length -= 60;
        }

        // Emit remainder.
        if (length < 12) {
            op = ref EmitCopyAtMost64LenLessThan12(ref op, offset, length);
        } else {
            op = ref EmitCopyAtMost64LenGreaterThanOrEqualTo12(ref op, offset, length);
        }
        return ref op;
    }

    /// <summary>
    /// Find the largest n such that
    ///
    ///   s1[0,n-1] == s2[0,n-1]
    ///   and n &lt;= (s2_limit - s2).
    ///
    /// Return (n, n &lt; 8).
    /// Reads up to and including *s2_limit but not beyond.
    /// Does not read *(s1 + (s2_limit - s2)) or beyond.
    /// Requires that s2_limit &gt;= s2.
    ///
    /// In addition populate *data with the next 5 bytes from the end of the match.
    /// This is only done if 8 bytes are available (s2_limit - s2 &gt;= 8). The point is
    /// that on some arch's this can be done faster in this routine than subsequent
    /// loading from s2 + n.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int matchLength, bool matchLengthLessThan8) FindMatchLength(
        ref readonly byte s1, ref readonly byte s2, ref readonly byte s2Limit, ref ulong data)
    {
        DebugExtensions.Assert(!Unsafe.IsAddressLessThan(in s2Limit, in s2));

        if (BitConverter.IsLittleEndian && IntPtr.Size == 8)
        {
            // Special implementation for 64-bit little endian processors (i.e. Intel/AMD x64)
            return FindMatchLengthX64(in s1, in s2, in s2Limit, ref data);
        }

        int matched = 0;

        while (Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)4
               && Helpers.UnsafeReadUInt32(in s2) == Helpers.UnsafeReadUInt32(in Unsafe.Add(in s1, matched)))
        {
            s2 = ref Unsafe.Add(in s2, 4);
            matched += 4;
        }

        if (BitConverter.IsLittleEndian && Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)4)
        {
            uint x = Helpers.UnsafeReadUInt32(in s2) ^ Helpers.UnsafeReadUInt32(in Unsafe.Add(in s1, matched));
            int matchingBits = Helpers.FindLsbSetNonZero(x);
            matched += matchingBits >> 3;
            s2 = ref Unsafe.Add(in s2, matchingBits >> 3);
        }
        else
        {
            while (Unsafe.IsAddressLessThan(in s2, in s2Limit) && Unsafe.Add(in s1, matched) == s2)
            {
                s2 = ref Unsafe.Add(in s2, 1);
                ++matched;
            }
        }

        if (Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)8)
        {
            data = Helpers.UnsafeReadUInt64(in s2);
        }

        return (matched, matched < 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int matchLength, bool matchLengthLessThan8) FindMatchLengthX64(
        ref readonly byte s1, ref readonly byte s2, ref readonly byte s2Limit, ref ulong data)
    {
        nint matched = 0;

        // This block isn't necessary for correctness; we could just start looping
        // immediately.  As an optimization though, it is useful.  It creates some not
        // uncommon code paths that determine, without extra effort, whether the match
        // length is less than 8.
        if (Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)16)
        {
            ulong a1 = Helpers.UnsafeReadUInt64(in s1);
            ulong a2 = Helpers.UnsafeReadUInt64(in s2);

            if (a1 != a2)
            {
                ulong xorval = a1 ^ a2;
                int shift = Helpers.FindLsbSetNonZero(xorval);
                int matchedBytes = shift >> 3;

                ulong a3 = Helpers.UnsafeReadUInt64(in Unsafe.Add(in s2, 4));
                a2 = unchecked((uint)xorval) == 0 ? a3 : a2;

                data = a2 >> (shift & (3 * 8));
                return (matchedBytes, true);
            }
            else
            {
                matched = 8;
                s2 = ref Unsafe.Add(in s2, 8);
            }
        }

        // Find out how long the match is. We loop over the data 64 bits at a
        // time until we find a 64-bit block that doesn't match; then we find
        // the first non-matching bit and use that to calculate the total
        // length of the match.
        while (Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)16)
        {
            ulong a1 = Helpers.UnsafeReadUInt64(in Unsafe.Add(in s1, matched));
            ulong a2 = Helpers.UnsafeReadUInt64(in s2);
            if (a1 == a2)
            {
                s2 = ref Unsafe.Add(in s2, 8);
                matched += 8;
            }
            else
            {
                ulong xorval = a1 ^ a2;
                int shift = Helpers.FindLsbSetNonZero(xorval);
                int matchedBytes = shift >> 3;

                ulong a3 = Helpers.UnsafeReadUInt64(in Unsafe.Add(in s2, 4));
                a2 = unchecked((uint)xorval) == 0 ? a3 : a2;

                data = a2 >> (shift & (3 * 8));
                matched += matchedBytes;
                DebugExtensions.Assert(matched >= 8);
                return ((int)matched, false);
            }
        }

        while (Unsafe.IsAddressLessThan(in s2, in s2Limit))
        {
            if (Unsafe.Add(in s1, matched) == s2)
            {
                s2 = ref Unsafe.Add(in s2, 1);
                matched++;
            }
            else
            {
                if (Unsafe.ByteOffset(in s2, in s2Limit) >= (nint)8)
                {
                    data = Helpers.UnsafeReadUInt64(in s2);
                }

                return ((int)matched, matched < 8);
            }
        }

        return ((int)matched, matched < 8);
    }

    #endregion
}
