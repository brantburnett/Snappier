﻿using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
#if NET6_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.Intrinsics.X86.Sse2;
using static System.Runtime.Intrinsics.X86.Ssse3;
#endif

namespace Snappier.Internal
{
    internal class CopyHelpers
    {
        #if NET6_0_OR_GREATER

        /// <summary>
        /// This is a table of shuffle control masks that can be used as the source
        /// operand for PSHUFB to permute the contents of the destination XMM register
        /// into a repeating byte pattern.
        /// </summary>
        private static readonly Vector128<byte>[] PshufbFillPatterns = {
            Vector128.Create((byte) 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            Vector128.Create((byte) 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1),
            Vector128.Create((byte) 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0),
            Vector128.Create((byte) 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3),
            Vector128.Create((byte) 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0),
            Vector128.Create((byte) 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3),
            Vector128.Create((byte) 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> LoadVector128Unsafe(ref byte source)
        {
#if NET7_0_OR_GREATER
            // Use the intrinsic for .NET 7
            return Vector128.LoadUnsafe(ref source);
#else
            // Fallback for .NET 6
            return Unsafe.ReadUnaligned<Vector128<byte>>(ref source);
#endif
        }

        /// <summary>
        /// j * (16 / j) for all j from 0 to 7. 0 is not actually used.
        /// </summary>
        private static readonly byte[] PatternSizeTable = {0, 16, 16, 15, 16, 15, 12, 14};

#endif

        /// <summary>
        /// Copy [src, src+(opEnd-op)) to [op, (opEnd-op)) but faster than
        /// IncrementalCopySlow. buf_limit is the address past the end of the writable
        /// region of the buffer. May write past opEnd, but won't write past bufferEnd.
        /// </summary>
        /// <param name="source">Pointer to the source point in the buffer.</param>
        /// <param name="op">Pointer to the destination point in the buffer.</param>
        /// <param name="opEnd">Pointer to the end of the area to write in the buffer.</param>
        /// <param name="bufferEnd">Pointer past the end of the buffer.</param>
        /// <remarks>
        /// Fixing the PshufbFillPatterns array for use in the SSSE3 optimized route is expensive, so we
        /// do that in the outer loop in <see cref="SnappyDecompressor.DecompressAllTags"/> and pass the pointer
        /// to this method. This makes the logic a bit more confusing, but is a significant performance boost.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementalCopy(ref byte source, ref byte op, ref byte opEnd, ref byte bufferEnd)
        {
            Debug.Assert(Unsafe.IsAddressLessThan(ref source, ref op));
            Debug.Assert(!Unsafe.IsAddressGreaterThan(ref op, ref opEnd));
            Debug.Assert(!Unsafe.IsAddressGreaterThan(ref opEnd, ref bufferEnd));
            // NOTE: The copy tags use 3 or 6 bits to store the copy length, so len <= 64.
            Debug.Assert(Unsafe.ByteOffset(ref op, ref opEnd) <= (nint) 64);
            // NOTE: In practice the compressor always emits len >= 4, so it is ok to
            // assume that to optimize this function, but this is not guaranteed by the
            // compression format, so we have to also handle len < 4 in case the input
            // does not satisfy these conditions.

            int patternSize = (int) Unsafe.ByteOffset(ref source, ref op);

            if (patternSize < 8)
            {
#if NET6_0_OR_GREATER
                if (Ssse3.IsSupported) // SSSE3
                {
                    // Load the first eight bytes into an 128-bit XMM register, then use PSHUFB
                    // to permute the register's contents in-place into a repeating sequence of
                    // the first "pattern_size" bytes.
                    // For example, suppose:
                    //    src       == "abc"
                    //    op        == op + 3
                    // After _mm_shuffle_epi8(), "pattern" will have five copies of "abc"
                    // followed by one byte of slop: abcabcabcabcabca.
                    //
                    // The non-SSE fallback implementation suffers from store-forwarding stalls
                    // because its loads and stores partly overlap. By expanding the pattern
                    // in-place, we avoid the penalty.

                    if (!Unsafe.IsAddressGreaterThan(ref op, ref Unsafe.Subtract(ref bufferEnd, 16)))
                    {
                        var shuffleMask = PshufbFillPatterns[patternSize - 1];
                        var srcPattern = LoadVector128Unsafe(ref source);
                        var pattern = Shuffle(srcPattern, shuffleMask);

                        // Get the new pattern size now that we've repeated it
                        patternSize = PatternSizeTable[patternSize];

                        // If we're getting to the very end of the buffer, don't overrun
                        ref byte loopEnd = ref Unsafe.Subtract(ref bufferEnd, 15);
                        if (Unsafe.IsAddressGreaterThan(ref loopEnd, ref opEnd))
                        {
                            loopEnd = ref opEnd;
                        }

                        while (Unsafe.IsAddressLessThan(ref op, ref loopEnd))
                        {
                            Store((byte*) Unsafe.AsPointer(ref op), pattern);
                            op = ref Unsafe.Add(ref op, patternSize);
                        }

                        if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
                        {
                            return;
                        }
                    }

                    IncrementalCopySlow(ref source, ref op, ref opEnd);
                    return;
                }
                else
                {
#endif
                    // No SSSE3 Fallback

                    // If plenty of buffer space remains, expand the pattern to at least 8
                    // bytes. The way the following loop is written, we need 8 bytes of buffer
                    // space if pattern_size >= 4, 11 bytes if pattern_size is 1 or 3, and 10
                    // bytes if pattern_size is 2.  Precisely encoding that is probably not
                    // worthwhile; instead, invoke the slow path if we cannot write 11 bytes
                    // (because 11 are required in the worst case).
                    if (!Unsafe.IsAddressGreaterThan(ref op, ref Unsafe.Subtract(ref bufferEnd, 11)))
                    {
                        while (patternSize < 8)
                        {
                            UnalignedCopy64(ref source, ref op);
                            op = ref Unsafe.Add(ref op, patternSize);
                            patternSize *= 2;
                        }

                        if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
                        {
                            return;
                        }
                    }
                    else
                    {
                        IncrementalCopySlow(ref source, ref op, ref opEnd);
                        return;
                    }
#if NET6_0_OR_GREATER
                }
#endif
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            Debug.Assert(patternSize >= 8);

            // Copy 2x 8 bytes at a time. Because op - src can be < 16, a single
            // UnalignedCopy128 might overwrite data in op. UnalignedCopy64 is safe
            // because expanding the pattern to at least 8 bytes guarantees that
            // op - src >= 8.
            //
            // Typically, the op_limit is the gating factor so try to simplify the loop
            // based on that.
            if (!Unsafe.IsAddressGreaterThan(ref opEnd, ref Unsafe.Subtract(ref bufferEnd, 16)))
            {
                UnalignedCopy64(ref source, ref op);
                UnalignedCopy64(ref Unsafe.Add(ref  source, 8), ref Unsafe.Add(ref op, 8));

                if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 16))) {
                    UnalignedCopy64(ref Unsafe.Add(ref source, 16), ref Unsafe.Add(ref op, 16));
                    UnalignedCopy64(ref Unsafe.Add(ref source, 24), ref Unsafe.Add(ref op, 24));
                }
                if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 32))) {
                    UnalignedCopy64(ref Unsafe.Add(ref source, 32), ref Unsafe.Add(ref op, 32));
                    UnalignedCopy64(ref Unsafe.Add(ref source, 40), ref Unsafe.Add(ref op, 40));
                }
                if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 48))) {
                    UnalignedCopy64(ref Unsafe.Add(ref source, 48), ref Unsafe.Add(ref op, 48));
                    UnalignedCopy64(ref Unsafe.Add(ref source, 56), ref Unsafe.Add(ref op, 56));
                }

                return;
            }

            // Fall back to doing as much as we can with the available slop in the
            // buffer.

            for (ref byte loopEnd = ref Unsafe.Subtract(ref bufferEnd, 16);
                 Unsafe.IsAddressLessThan(ref op, ref loopEnd);
                 op = ref Unsafe.Add(ref op, 16), source = ref Unsafe.Add(ref source, 16))
            {
                UnalignedCopy64(ref source, ref op);
                UnalignedCopy64(ref Unsafe.Add(ref source, 8), ref Unsafe.Add(ref op, 8));
            }

            if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
            {
                return;
            }

            // We only take this branch if we didn't have enough slop and we can do a
            // single 8 byte copy.
            if (!Unsafe.IsAddressGreaterThan(ref op, ref Unsafe.Subtract(ref bufferEnd, 8)))
            {
                UnalignedCopy64(ref source, ref op);
                source = ref Unsafe.Add(ref source, 8);
                op = ref Unsafe.Add(ref op, 8);
            }

            IncrementalCopySlow(ref source, ref op, ref opEnd);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void IncrementalCopySlow(ref byte source, ref byte op, ref byte opEnd)
        {
            while (Unsafe.IsAddressLessThan(ref op, ref opEnd))
            {
                op = source;
                op = ref Unsafe.Add(ref op, 1);
                source = ref Unsafe.Add(ref source, 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnalignedCopy64(ref byte source, ref byte destination)
        {
            // Stackalloc may prevent inlining, so use an 8-byte long for the buffer
            Unsafe.SkipInit(out long tempStackVar);
            ref byte temp = ref Unsafe.As<long, byte>(ref tempStackVar);

            Unsafe.CopyBlockUnaligned(ref temp, ref source, 8);
            Unsafe.CopyBlockUnaligned(ref destination, ref temp, 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnalignedCopy128(ref byte source, ref byte destination)
        {
            // Stackalloc may prevent inlining, so use a 16-byte Guid for the buffer
            Unsafe.SkipInit(out Guid tempStackVar);
            ref byte temp = ref Unsafe.As<Guid, byte>(ref tempStackVar);

            Unsafe.CopyBlockUnaligned(ref temp, ref source, 16);
            Unsafe.CopyBlockUnaligned(ref destination, ref temp, 16);
        }
    }
}
