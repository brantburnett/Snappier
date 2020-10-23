using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    internal class SnappyCompressor : IDisposable
    {
        private HashTable? _workingMemory = new HashTable();

        public int Compress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (output.Length < Helpers.MaxCompressedLength(input.Length))
            {
                throw new ArgumentException("Insufficient output buffer", nameof(output));
            }
            if (_workingMemory == null)
            {
                throw new ObjectDisposedException(nameof(SnappyCompressor));
            }

            _workingMemory.EnsureCapacity(input.Length);

            int bytesWritten = WriteUncompressedLength(output, input.Length);
            output = output.Slice(bytesWritten);

            while (input.Length > 0)
            {
                var fragment = input.Slice(0, Math.Min(input.Length, (int)Constants.BlockSize));

                var hashTable = _workingMemory.GetHashTable(fragment.Length);

                var maxOutput = Helpers.MaxCompressedLength(fragment.Length);

                if (output.Length >= maxOutput)
                {
                    var written = CompressFragment(fragment, output, hashTable);

                    output = output.Slice(written);
                    bytesWritten += written;
                }
                else
                {
                    using var scratch = MemoryPool<byte>.Shared.Rent(maxOutput);

                    var written = CompressFragment(fragment, scratch.Memory.Span, hashTable);

                    scratch.Memory.Span.Slice(0, written).CopyTo(output);
                    output = output.Slice(written);
                    bytesWritten += written;
                }

                input = input.Slice(fragment.Length);
            }

            return bytesWritten;
        }

        public void Dispose()
        {
            _workingMemory?.Dispose();
            _workingMemory = null;
        }

        private static int WriteUncompressedLength(Span<byte> output, int length)
        {
            const int b = 0b1000_0000;

            unchecked
            {
                if (length < (1 << 7))
                {
                    output[0] = (byte) length;
                    return 1;
                }
                else if (length < (1 << 14))
                {
                    output[0] = (byte) (length | b);
                    output[1] = (byte) (length >> 7);
                    return 2;
                }
                else if (length < (1 << 21))
                {
                    output[0] = (byte) (length | b);
                    output[1] = (byte) ((length >> 7) | b);
                    output[2] = (byte) (length >> 14);
                    return 3;
                }
                else if (length < (1 << 28))
                {
                    output[0] = (byte) (length | b);
                    output[1] = (byte) ((length >> 7) | b);
                    output[2] = (byte) ((length >> 14) | b);
                    output[3] = (byte) (length >> 21);
                    return 4;
                }
                else
                {
                    output[0] = (byte) (length | b);
                    output[1] = (byte) ((length >> 7) | b);
                    output[2] = (byte) ((length >> 14) | b);
                    output[3] = (byte) ((length >> 21) | b);
                    output[4] = (byte) (length >> 28);
                    return 5;
                }
            }
        }

        #region CompressFragment

        private static unsafe int CompressFragment(ReadOnlySpan<byte> input, Span<byte> output, Span<ushort> tableSpan)
        {
            unchecked
            {
                Debug.Assert(input.Length <= Constants.BlockSize);
                Debug.Assert((tableSpan.Length & (tableSpan.Length - 1)) == 0); // table must be power of two

                int shift = 32 - Helpers.Log2Floor((uint) tableSpan.Length);

                Debug.Assert(uint.MaxValue >> shift == tableSpan.Length - 1);

                fixed (byte* inputStart = input)
                {
                    var inputEnd = inputStart + input.Length;
                    var ip = inputStart;

                    fixed (byte* outputStart = output)
                    {
                        fixed (ushort* table = tableSpan)
                        {
                            var op = outputStart;

                            if (input.Length >= Constants.InputMarginBytes)
                            {
                                var ipLimit = inputEnd - Constants.InputMarginBytes;

                                for (var preload = Helpers.UnsafeReadUInt32(ip + 1);;)
                                {
                                    // Bytes in [nextEmit, ip) will be emitted as literal bytes.  Or
                                    // [nextEmit, ipEnd) after the main loop.
                                    byte* nextEmit = ip++;
                                    var data = Helpers.UnsafeReadUInt64(ip);

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
                                    uint skip = 32;

                                    byte* candidate;
                                    if (ipLimit - ip >= 16)
                                    {
                                        var delta = ip - inputStart;
                                        for (int j = 0; j < 4; ++j)
                                        {
                                            for (int k = 0; k < 4; ++k)
                                            {
                                                int i = 4 * j + k;
                                                // These for-loops are meant to be unrolled. So we can freely
                                                // special case the first iteration to use the value already
                                                // loaded in preload.

                                                uint dword = i == 0 ? preload : (uint) data;
                                                Debug.Assert(dword == Helpers.UnsafeReadUInt32(ip + i));
                                                long hash = Helpers.HashBytes(dword, shift);
                                                candidate = inputStart + table[hash];
                                                Debug.Assert(candidate >= inputStart);
                                                Debug.Assert(candidate < ip + i);
                                                table[hash] = (ushort) (delta + i);

                                                if (Helpers.UnsafeReadUInt32(candidate) == dword)
                                                {
                                                    *op = (byte) (Constants.Literal | (i << 2));
                                                    CopyHelpers.UnalignedCopy128(nextEmit, op + 1);
                                                    ip += i;
                                                    op = op + i + 2;
                                                    goto emit_match;
                                                }

                                                data >>= 8;
                                            }

                                            data = Helpers.UnsafeReadUInt64(ip + 4 * j + 4);
                                        }

                                        ip += 16;
                                        skip += 16;
                                    }

                                    while (true)
                                    {
                                        Debug.Assert((uint) data == Helpers.UnsafeReadUInt32(ip));
                                        long hash = Helpers.HashBytes((uint) data, shift);
                                        uint bytesBetweenHashLookups = skip >> 5;
                                        skip += bytesBetweenHashLookups;

                                        byte* nextIp = ip + bytesBetweenHashLookups;
                                        if (nextIp > ipLimit)
                                        {
                                            ip = nextEmit;
                                            goto emit_remainder;
                                        }

                                        candidate = inputStart + table[hash];
                                        Debug.Assert(candidate >= inputStart);
                                        Debug.Assert(candidate < ip);

                                        table[hash] = (ushort) (ip - inputStart);
                                        if ((uint) data == Helpers.UnsafeReadUInt32(candidate))
                                        {
                                            break;
                                        }

                                        data = Helpers.UnsafeReadUInt32(nextIp);
                                        ip = nextIp;
                                    }

                                    // Step 2: A 4-byte match has been found.  We'll later see if more
                                    // than 4 bytes match.  But, prior to the match, input
                                    // bytes [next_emit, ip) are unmatched.  Emit them as "literal bytes."
                                    Debug.Assert(nextEmit + 16 <= inputEnd);
                                    op = EmitLiteralFast(op, nextEmit, (uint) (ip - nextEmit));

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
                                        byte* emitBase = ip;

                                        var (matchLength, matchLengthLessThan8) =
                                            FindMatchLength(candidate + 4, ip + 4, inputEnd, ref data);

                                        long matched = 4 + matchLength;
                                        ip += matched;

                                        long offset = emitBase - candidate;
                                        if (matchLengthLessThan8)
                                        {
                                            op = EmitCopyLenLessThan12(op, offset, matched);
                                        }
                                        else
                                        {
                                            op = EmitCopyLenGreaterThanOrEqualTo12(op, offset, matched);
                                        }

                                        if (ip >= ipLimit)
                                        {
                                            goto emit_remainder;
                                        }

                                        // Expect 5 bytes to match
                                        Debug.Assert((data & 0xfffffffffful) ==
                                                     (Helpers.UnsafeReadUInt64(ip) & 0xfffffffffful));

                                        // We are now looking for a 4-byte match again.  We read
                                        // table[Hash(ip, shift)] for that.  To improve compression,
                                        // we also update table[Hash(ip - 1, shift)] and table[Hash(ip, shift)].
                                        table[Helpers.HashBytes(Helpers.UnsafeReadUInt32(ip - 1), shift)] =
                                            (ushort) (ip - inputStart - 1);
                                        long hash = Helpers.HashBytes((uint) data, shift);
                                        candidate = inputStart + table[hash];
                                        table[hash] = (ushort) (ip - inputStart);
                                    } while ((uint) data == Helpers.UnsafeReadUInt32(candidate));

                                    // Because the least significant 5 bytes matched, we can utilize data
                                    // for the next iteration.
                                    preload = (uint) (data >> 8);
                                }
                            }

                            emit_remainder:
                            // Emit the remaining bytes as a literal
                            if (ip < inputEnd)
                            {
                                op = EmitLiteralSlow(op, ip, (uint) (inputEnd - ip));
                            }

                            return (int) (op - outputStart);
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitLiteralFast(byte* op, byte* literal, uint length)
        {
            Debug.Assert(length > 0);

            if (length <= 16)
            {
                uint n = length - 1;
                *op++ = unchecked((byte)(Constants.Literal | (n << 2)));

                CopyHelpers.UnalignedCopy128(literal, op);
                return op + length;
            }

            return EmitLiteralSlow(op, literal, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitLiteralSlow(byte* op, byte* literal, uint length)
        {
            uint n = length - 1;
            if (n < 60)
            {
                *op++ = unchecked((byte) (Constants.Literal | (n << 2)));
            }
            else
            {
                int count = (Helpers.Log2Floor(n) >> 3) + 1;

                Debug.Assert(count >= 1);
                Debug.Assert(count <= 4);
                *op++ = unchecked((byte)(Constants.Literal | ((59 + count) << 2)));

                // Encode in upcoming bytes.
                // Write 4 bytes, though we may care about only 1 of them. The output buffer
                // is guaranteed to have at least 3 more spaces left as 'len >= 61' holds
                // here and there is a std::memcpy() of size 'len' below.
                Helpers.UnsafeWriteUInt32(op, n);
                op += count;
            }

            Unsafe.CopyBlockUnaligned(op, literal, length);
            return op + length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitCopyAtMost64LenLessThan12(byte* op, long offset, long length)
        {
            Debug.Assert(length <= 64);
            Debug.Assert(length >= 4);
            Debug.Assert(offset < 65536);
            Debug.Assert(length < 12);

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
                Helpers.UnsafeWriteUInt32(op, u);
            }

            return op + (offset < 2048 ? 2 : 3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitCopyAtMost64LenGreaterThanOrEqualTo12(byte* op, long offset, long length)
        {
            Debug.Assert(length <= 64);
            Debug.Assert(length >= 4);
            Debug.Assert(offset < 65536);
            Debug.Assert(length >= 12);

            // Write 4 bytes, though we only care about 3 of them.  The output buffer
            // is required to have some slack, so the extra byte won't overrun it.
            var u = unchecked((uint)(Constants.Copy2ByteOffset + ((length - 1) << 2) + (offset << 8)));
            Helpers.UnsafeWriteUInt32(op, u);
            return op + 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitCopyLenLessThan12(byte* op, long offset, long length)
        {
            Debug.Assert(length < 12);

            return EmitCopyAtMost64LenLessThan12(op, offset, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte* EmitCopyLenGreaterThanOrEqualTo12(byte* op, long offset, long length)
        {
            Debug.Assert(length >= 12);

            // A special case for len <= 64 might help, but so far measurements suggest
            // it's in the noise.

            // Emit 64 byte copies but make sure to keep at least four bytes reserved.
            while (length >= 68)
            {
                op = EmitCopyAtMost64LenGreaterThanOrEqualTo12(op, offset, 64);
                length -= 64;
            }

            // One or two copies will now finish the job.
            if (length > 64) {
                op = EmitCopyAtMost64LenGreaterThanOrEqualTo12(op, offset, 60);
                length -= 60;
            }

            // Emit remainder.
            if (length < 12) {
                op = EmitCopyAtMost64LenLessThan12(op, offset, length);
            } else {
                op = EmitCopyAtMost64LenGreaterThanOrEqualTo12(op, offset, length);
            }
            return op;
        }

        /// <summary>
        /// Find the largest n such that
        ///
        ///   s1[0,n-1] == s2[0,n-1]
        ///   and n &lt;= (s2_limit - s2).
        ///
        /// Return (n, n &lt; 8).
        /// Does not read *s2_limit or beyond.
        /// Does not read *(s1 + (s2_limit - s2)) or beyond.
        /// Requires that s2_limit &gt;= s2.
        ///
        /// In addition populate *data with the next 5 bytes from the end of the match.
        /// This is only done if 8 bytes are available (s2_limit - s2 &gt;= 8). The point is
        /// that on some arch's this can be done faster in this routine than subsequent
        /// loading from s2 + n.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long matchLength, bool matchLengthLessThan8) FindMatchLength(
            byte* s1, byte* s2, byte* s2Limit, ref ulong data)
        {
            Debug.Assert(s2Limit >= s2);

            int matched = 0;

            while (s2 <= s2Limit - 4 && Helpers.UnsafeReadUInt32(s2) == Helpers.UnsafeReadUInt32(s1 + matched))
            {
                s2 += 4;
                matched += 4;
            }

            if (BitConverter.IsLittleEndian && s2 <= s2Limit - 4)
            {
                uint x = Helpers.UnsafeReadUInt32(s2) ^ Helpers.UnsafeReadUInt32(s1 + matched);
                int matchingBits = Helpers.FindLsbSetNonZero(x);
                matched += matchingBits >> 3;
                s2 += matchingBits >> 3;
            }
            else
            {
                while (s2 < s2Limit && s1[matched] == *s2)
                {
                    ++s2;
                    ++matched;
                }
            }

            if (s2 <= s2Limit - 8)
            {
                data = Helpers.UnsafeReadUInt64(s2);
            }

            return (matched, matched < 8);
        }

        #endregion
    }
}
