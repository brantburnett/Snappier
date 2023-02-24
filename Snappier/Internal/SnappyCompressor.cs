using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    internal class SnappyCompressor2 : IDisposable
    {
        private HashTable? _workingMemory = new();

        public int Compress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (_workingMemory == null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(SnappyCompressor));
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
                    var scratch = ArrayPool<byte>.Shared.Rent(maxOutput);
                    try
                    {
                        int written = CompressFragment(fragment, scratch.AsSpan(), hashTable);
                        if (output.Length < written)
                        {
                            ThrowHelper.ThrowArgumentException("Insufficient output buffer", nameof(output));
                        }

                        scratch.AsSpan(0, written).CopyTo(output);
                        output = output.Slice(written);
                        bytesWritten += written;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(scratch);
                    }
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

        private static int CompressFragment(ReadOnlySpan<byte> input, Span<byte> output, Span<ushort> tableSpan)
        {
            unchecked
            {
                Debug.Assert(input.Length <= Constants.BlockSize);
                Debug.Assert((tableSpan.Length & (tableSpan.Length - 1)) == 0); // table must be power of two

                uint mask = (uint)(2 * (tableSpan.Length - 1));

                ref byte inputStart = ref Unsafe.AsRef(in input[0]);
                // Last byte of the input, not one byte past the end, to avoid issues on GC moves
                ref byte inputEnd = ref Unsafe.Add(ref inputStart, input.Length - 1);
                ref byte ip = ref inputStart;

                ref byte op = ref output[0];
                ref ushort table = ref tableSpan[0];

                if (input.Length >= Constants.InputMarginBytes)
                {
                    ref byte ipLimit = ref Unsafe.Subtract(ref inputEnd, Constants.InputMarginBytes - 1);

                    for (uint preload = Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref ip, 1));;)
                    {
                        // Bytes in [nextEmit, ip) will be emitted as literal bytes.  Or
                        // [nextEmit, ipEnd) after the main loop.
                        ref byte nextEmit = ref ip;
                        ip = ref Unsafe.Add(ref ip, 1);
                        ulong data = Helpers.UnsafeReadUInt64(ref ip);

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

                        ref byte candidate = ref Unsafe.NullRef<byte>();
                        if (Unsafe.ByteOffset(ref ip, ref ipLimit) >= (nint) 16)
                        {
                            nint delta = Unsafe.ByteOffset(ref inputStart, ref ip);
                            for (int j = 0; j < 16; j += 4)
                            {
                                // Manually unroll this loop into chunks of 4

                                uint dword = j == 0 ? preload : (uint) data;
                                Debug.Assert(dword == Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref ip, j)));
                                ref ushort tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                                candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                                Debug.Assert(!Unsafe.IsAddressLessThan(ref candidate, ref inputStart));
                                Debug.Assert(Unsafe.IsAddressLessThan(ref candidate, ref Unsafe.Add(ref ip, j)));
                                tableEntry = (ushort) (delta + j);

                                if (Helpers.UnsafeReadUInt32(ref candidate) == dword)
                                {
                                    op = (byte) (Constants.Literal | (j << 2));
                                    CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op,  1));
                                    ip = ref Unsafe.Add(ref ip, j);
                                    op = ref Unsafe.Add(ref op, j + 2);
                                    goto emit_match;
                                }

                                int i1 = j + 1;
                                dword = (uint)(data >> 8);
                                Debug.Assert(dword == Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref ip, i1)));
                                tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                                candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                                Debug.Assert(!Unsafe.IsAddressLessThan(ref candidate, ref inputStart));
                                Debug.Assert(Unsafe.IsAddressLessThan(ref candidate, ref Unsafe.Add(ref ip, i1)));
                                tableEntry = (ushort) (delta + i1);

                                if (Helpers.UnsafeReadUInt32(ref candidate) == dword)
                                {
                                    op = (byte) (Constants.Literal | (i1 << 2));
                                    CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                    ip = ref Unsafe.Add(ref ip, i1);
                                    op = ref Unsafe.Add(ref op, i1 + 2);
                                    goto emit_match;
                                }

                                int i2 = j + 2;
                                dword = (uint)(data >> 16);
                                Debug.Assert(dword == Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref ip, i2)));
                                tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                                candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                                Debug.Assert(!Unsafe.IsAddressLessThan(ref candidate, ref inputStart));
                                Debug.Assert(Unsafe.IsAddressLessThan(ref candidate, ref Unsafe.Add(ref ip, i2)));
                                tableEntry = (ushort) (delta + i2);

                                if (Helpers.UnsafeReadUInt32(ref candidate) == dword)
                                {
                                    op = (byte) (Constants.Literal | (i2 << 2));
                                    CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                    ip = ref Unsafe.Add(ref ip, i2);
                                    op = ref Unsafe.Add(ref op, i2 + 2);
                                    goto emit_match;
                                }

                                int i3 = j + 3;
                                dword = (uint)(data >> 24);
                                Debug.Assert(dword == Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref ip, i3)));
                                tableEntry = ref HashTable.TableEntry(ref table, dword, mask);
                                candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                                Debug.Assert(!Unsafe.IsAddressLessThan(ref candidate, ref inputStart));
                                Debug.Assert(Unsafe.IsAddressLessThan(ref candidate, ref Unsafe.Add(ref ip, i3)));
                                tableEntry = (ushort) (delta + i3);

                                if (Helpers.UnsafeReadUInt32(ref candidate) == dword)
                                {
                                    op = (byte) (Constants.Literal | (i3 << 2));
                                    CopyHelpers.UnalignedCopy128(in nextEmit, ref Unsafe.Add(ref op, 1));
                                    ip = ref Unsafe.Add(ref ip, i3);
                                    op = ref Unsafe.Add(ref op, i3 + 2);
                                    goto emit_match;
                                }

                                data = Helpers.UnsafeReadUInt64(ref Unsafe.Add(ref ip, j + 4));
                            }

                            ip = ref Unsafe.Add(ref ip, 16);
                            skip += 16;
                        }

                        while (true)
                        {
                            Debug.Assert((uint) data == Helpers.UnsafeReadUInt32(ref ip));
                            ref ushort tableEntry = ref HashTable.TableEntry(ref table, (uint) data, mask);
                            int bytesBetweenHashLookups = skip >> 5;
                            skip += bytesBetweenHashLookups;

                            ref byte nextIp = ref Unsafe.Add(ref ip, bytesBetweenHashLookups);
                            if (Unsafe.IsAddressGreaterThan(ref nextIp, ref ipLimit))
                            {
                                ip = ref nextEmit;
                                goto emit_remainder;
                            }

                            candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                            Debug.Assert(!Unsafe.IsAddressLessThan(ref candidate, ref inputStart));
                            Debug.Assert(Unsafe.IsAddressLessThan(ref candidate, ref ip));

                            tableEntry = (ushort) Unsafe.ByteOffset(ref inputStart, ref ip);
                            if ((uint) data == Helpers.UnsafeReadUInt32(ref candidate))
                            {
                                break;
                            }

                            data = Helpers.UnsafeReadUInt32(ref nextIp);
                            ip = ref nextIp;
                        }

                        // Step 2: A 4-byte match has been found.  We'll later see if more
                        // than 4 bytes match.  But, prior to the match, input
                        // bytes [next_emit, ip) are unmatched.  Emit them as "literal bytes."
                        Debug.Assert(!Unsafe.IsAddressGreaterThan(ref Unsafe.Add(ref nextEmit, 16), ref Unsafe.Add(ref inputEnd, 1)));
                        op = ref EmitLiteralFast(ref op, ref nextEmit, (uint) Unsafe.ByteOffset(ref nextEmit, ref ip));

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
                            ref byte emitBase = ref ip;

                            var (matchLength, matchLengthLessThan8) =
                                FindMatchLength(ref Unsafe.Add(ref candidate, 4), ref Unsafe.Add(ref ip, 4), ref inputEnd, ref data);

                            int matched = 4 + matchLength;
                            ip = ref Unsafe.Add(ref ip, matched);

                            nint offset = Unsafe.ByteOffset(ref candidate, ref emitBase);
                            if (matchLengthLessThan8)
                            {
                                op = ref EmitCopyLenLessThan12(ref op, offset, matched);
                            }
                            else
                            {
                                op = ref EmitCopyLenGreaterThanOrEqualTo12(ref op, offset, matched);
                            }

                            if (!Unsafe.IsAddressLessThan(ref ip, ref ipLimit))
                            {
                                goto emit_remainder;
                            }

                            // Expect 5 bytes to match
                            Debug.Assert((data & 0xfffffffffful) ==
                                         (Helpers.UnsafeReadUInt64(ref ip) & 0xfffffffffful));

                            // We are now looking for a 4-byte match again.  We read
                            // table[Hash(ip, mask)] for that.  To improve compression,
                            // we also update table[Hash(ip - 1, mask)] and table[Hash(ip, mask)].
                            HashTable.TableEntry(ref table, Helpers.UnsafeReadUInt32(ref Unsafe.Subtract(ref ip, 1)), mask) =
                                (ushort) (Unsafe.ByteOffset(ref inputStart, ref ip) - 1);
                            ref ushort tableEntry = ref HashTable.TableEntry(ref table, (uint) data, mask);
                            candidate = ref Unsafe.Add(ref inputStart, tableEntry);
                            tableEntry = (ushort) Unsafe.ByteOffset(ref inputStart, ref ip);
                        } while ((uint) data == Helpers.UnsafeReadUInt32(ref candidate));

                        // Because the least significant 5 bytes matched, we can utilize data
                        // for the next iteration.
                        preload = (uint) (data >> 8);
                    }
                }

                emit_remainder:
                // Emit the remaining bytes as a literal
                if (!Unsafe.IsAddressGreaterThan(ref ip, ref inputEnd))
                {
                    op = ref EmitLiteralSlow(ref op, ref ip, (uint) Unsafe.ByteOffset(ref ip, ref inputEnd) + 1);
                }

                return (int) Unsafe.ByteOffset(ref output[0], ref op);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitLiteralFast(ref byte op, ref byte literal, uint length)
        {
            Debug.Assert(length > 0);

            if (length <= 16)
            {
                uint n = length - 1;
                op = unchecked((byte)(Constants.Literal | (n << 2)));
                op = ref Unsafe.Add(ref op, 1);

                CopyHelpers.UnalignedCopy128(in literal, ref op);
                return ref Unsafe.Add(ref op, length);
            }

            return ref EmitLiteralSlow(ref op, ref literal, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitLiteralSlow(ref byte op, ref byte literal, uint length)
        {
            uint n = length - 1;
            if (n < 60)
            {
                op = unchecked((byte) (Constants.Literal | (n << 2)));
                op = ref Unsafe.Add(ref op, 1);
            }
            else
            {
                int count = (Helpers.Log2Floor(n) >> 3) + 1;

                Debug.Assert(count >= 1);
                Debug.Assert(count <= 4);
                op = unchecked((byte)(Constants.Literal | ((59 + count) << 2)));
                op = ref Unsafe.Add(ref op, 1);

                // Encode in upcoming bytes.
                // Write 4 bytes, though we may care about only 1 of them. The output buffer
                // is guaranteed to have at least 3 more spaces left as 'len >= 61' holds
                // here and there is a std::memcpy() of size 'len' below.
                Helpers.UnsafeWriteUInt32(ref op, n);
                op = ref Unsafe.Add(ref op, count);
            }

            Unsafe.CopyBlockUnaligned(ref op, ref literal, length);
            return ref Unsafe.Add(ref op,  length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitCopyAtMost64LenLessThan12(ref byte op, long offset, long length)
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
                Helpers.UnsafeWriteUInt32(ref op, u);
            }

            return ref Unsafe.Add(ref op, offset < 2048 ? 2 : 3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitCopyAtMost64LenGreaterThanOrEqualTo12(ref byte op, long offset, long length)
        {
            Debug.Assert(length <= 64);
            Debug.Assert(length >= 4);
            Debug.Assert(offset < 65536);
            Debug.Assert(length >= 12);

            // Write 4 bytes, though we only care about 3 of them.  The output buffer
            // is required to have some slack, so the extra byte won't overrun it.
            var u = unchecked((uint)(Constants.Copy2ByteOffset + ((length - 1) << 2) + (offset << 8)));
            Helpers.UnsafeWriteUInt32(ref op, u);
            return ref Unsafe.Add(ref op, 3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitCopyLenLessThan12(ref byte op, long offset, long length)
        {
            Debug.Assert(length < 12);

            return ref EmitCopyAtMost64LenLessThan12(ref op, offset, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte EmitCopyLenGreaterThanOrEqualTo12(ref byte op, long offset, long length)
        {
            Debug.Assert(length >= 12);

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
        ///   and n &lt;= (s2_limit - s2 + 1).
        ///
        /// Return (n, n &lt; 8).
        /// Reads up to and including *s2_limit but not beyond.
        /// Does not read *(s1 + (s2_limit - s2 + 1)) or beyond.
        /// Requires that s2_limit+1 &gt;= s2.
        ///
        /// In addition populate *data with the next 5 bytes from the end of the match.
        /// This is only done if 8 bytes are available (s2_limit - s2 &gt;= 8). The point is
        /// that on some arch's this can be done faster in this routine than subsequent
        /// loading from s2 + n.
        /// </summary>
        /// <remarks>
        /// The reference implementation has s2Limit as one byte past the end of the input,
        /// but this implementation has it at the end of the input. This ensures that it always
        /// points within the array in case GC moves the array.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int matchLength, bool matchLengthLessThan8) FindMatchLength(
            ref byte s1, ref byte s2, ref byte s2Limit, ref ulong data)
        {
            Debug.Assert(!Unsafe.IsAddressLessThan(ref Unsafe.Add(ref s2Limit, 1), ref s2));

            int matched = 0;

            while (!Unsafe.IsAddressGreaterThan(ref s2, ref Unsafe.Subtract(ref s2Limit, 3))
                   && Helpers.UnsafeReadUInt32(ref s2) == Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref s1, matched)))
            {
                s2 = ref Unsafe.Add(ref s2, 4);
                matched += 4;
            }

            if (BitConverter.IsLittleEndian && !Unsafe.IsAddressGreaterThan(ref s2, ref Unsafe.Subtract(ref s2Limit, 3)))
            {
                uint x = Helpers.UnsafeReadUInt32(ref s2) ^ Helpers.UnsafeReadUInt32(ref Unsafe.Add(ref s1, matched));
                int matchingBits = Helpers.FindLsbSetNonZero(x);
                matched += matchingBits >> 3;
                s2 = ref Unsafe.Add(ref s2, matchingBits >> 3);
            }
            else
            {
                while (!Unsafe.IsAddressGreaterThan(ref s2, ref s2Limit) && Unsafe.Add(ref s1, matched) == s2)
                {
                    s2 = ref Unsafe.Add(ref s2, 1);
                    ++matched;
                }
            }

            if (!Unsafe.IsAddressGreaterThan(ref s2, ref Unsafe.Subtract(ref s2Limit, 7)))
            {
                data = Helpers.UnsafeReadUInt64(ref s2);
            }

            return (matched, matched < 8);
        }

        #endregion
    }
}
