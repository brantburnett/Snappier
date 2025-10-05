using System.Diagnostics;
using System.Runtime.CompilerServices;

#if !NETSTANDARD2_0
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.Intrinsics.X86.Ssse3;
#endif

namespace Snappier.Internal;

internal class CopyHelpers
{
#if !NETSTANDARD2_0

    // Raw bytes for PshufbFillPatterns. This syntax returns a ReadOnlySpan<byte> that references
    // directly to the static data within the DLL. This is only supported with bytes due to things
    // like byte-ordering on various architectures, so we can reference Vector128<byte> directly.
    // It is however safe to convert to Vector128<byte> so we'll do that below with some casts
    // that are elided by JIT.
    private static ReadOnlySpan<byte> PshufbFillPatternsAsBytes =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // Never referenced, here for padding
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0,
        0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
        0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0,
        0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0, 1, 2, 3,
        0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1
    ];

    /// <summary>
    /// This is a table of shuffle control masks that can be used as the source
    /// operand for PSHUFB to permute the contents of the destination XMM register
    /// into a repeating byte pattern.
    /// </summary>
    private static ReadOnlySpan<Vector128<byte>> PshufbFillPatterns
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => MemoryMarshal.CreateReadOnlySpan(
            reference: ref Unsafe.As<byte, Vector128<byte>>(ref MemoryMarshal.GetReference(PshufbFillPatternsAsBytes)),
            length: 8);
    }

    /// <summary>
    /// j * (16 / j) for all j from 0 to 7. 0 is not actually used.
    /// </summary>
    private static ReadOnlySpan<byte> PatternSizeTable => [0, 16, 16, 15, 16, 15, 12, 14];

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncrementalCopy(ref readonly byte source, ref byte op, ref byte opEnd, ref byte bufferEnd)
    {
        Debug.Assert(Unsafe.IsAddressLessThan(in source, in op));
        Debug.Assert(!Unsafe.IsAddressGreaterThan(ref op, ref opEnd));
        Debug.Assert(!Unsafe.IsAddressGreaterThan(ref opEnd, ref bufferEnd));
        // NOTE: The copy tags use 3 or 6 bits to store the copy length, so len <= 64.
        Debug.Assert(Unsafe.ByteOffset(ref op, ref opEnd) <= (nint) 64);
        // NOTE: In practice the compressor always emits len >= 4, so it is ok to
        // assume that to optimize this function, but this is not guaranteed by the
        // compression format, so we have to also handle len < 4 in case the input
        // does not satisfy these conditions.

        int patternSize = (int) Unsafe.ByteOffset(in source, in op);

        if (patternSize < 8)
        {
#if !NETSTANDARD2_0
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
                    Vector128<byte> shuffleMask = PshufbFillPatterns[patternSize];
                    Vector128<byte> srcPattern = Vector128.LoadUnsafe(in source);
                    Vector128<byte> pattern = Shuffle(srcPattern, shuffleMask);

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
                        pattern.StoreUnsafe(ref op);
                        op = ref Unsafe.Add(ref op, patternSize);
                    }

                    if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
                    {
                        return;
                    }
                }

                IncrementalCopySlow(in source, ref op, ref opEnd);
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
                        UnalignedCopy64(in source, ref op);
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
                    IncrementalCopySlow(in source, ref op, ref opEnd);
                    return;
                }
#if !NETSTANDARD2_0
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
            UnalignedCopy64(in source, ref op);
            UnalignedCopy64(in Unsafe.Add(in source, 8), ref Unsafe.Add(ref op, 8));

            if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 16))) {
                UnalignedCopy64(in Unsafe.Add(in source, 16), ref Unsafe.Add(ref op, 16));
                UnalignedCopy64(in Unsafe.Add(in source, 24), ref Unsafe.Add(ref op, 24));
            }
            if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 32))) {
                UnalignedCopy64(in Unsafe.Add(in source, 32), ref Unsafe.Add(ref op, 32));
                UnalignedCopy64(in Unsafe.Add(in source, 40), ref Unsafe.Add(ref op, 40));
            }
            if (Unsafe.IsAddressLessThan(ref op, ref Unsafe.Subtract(ref opEnd, 48))) {
                UnalignedCopy64(in Unsafe.Add(in source, 48), ref Unsafe.Add(ref op, 48));
                UnalignedCopy64(in Unsafe.Add(in source, 56), ref Unsafe.Add(ref op, 56));
            }

            return;
        }

        // Fall back to doing as much as we can with the available slop in the
        // buffer.

        for (ref byte loopEnd = ref Unsafe.Subtract(ref bufferEnd, 16);
             Unsafe.IsAddressLessThan(ref op, ref loopEnd);
             op = ref Unsafe.Add(ref op, 16), source = ref Unsafe.Add(in source, 16))
        {
            UnalignedCopy64(in source, ref op);
            UnalignedCopy64(in Unsafe.Add(in source, 8), ref Unsafe.Add(ref op, 8));
        }

        if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
        {
            return;
        }

        // We only take this branch if we didn't have enough slop and we can do a
        // single 8 byte copy.
        if (!Unsafe.IsAddressGreaterThan(ref op, ref Unsafe.Subtract(ref bufferEnd, 8)))
        {
            UnalignedCopy64(in source, ref op);
            source = ref Unsafe.Add(in source, 8);
            op = ref Unsafe.Add(ref op, 8);
        }

        IncrementalCopySlow(in source, ref op, ref opEnd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncrementalCopySlow(ref readonly byte source, ref byte op, ref byte opEnd)
    {
        while (Unsafe.IsAddressLessThan(ref op, ref opEnd))
        {
            op = source;
            op = ref Unsafe.Add(ref op, 1);
            source = ref Unsafe.Add(in source, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnalignedCopy64(ref readonly byte source, ref byte destination)
    {
        long tempStackVar = Unsafe.As<byte, long>(in source);
        Unsafe.As<byte, long>(ref destination) = tempStackVar;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnalignedCopy128(ref readonly byte source, ref byte destination)
    {
        Guid tempStackVar = Unsafe.As<byte, Guid>(in source);
        Unsafe.As<byte, Guid>(ref destination) = tempStackVar;
    }
}
