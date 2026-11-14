using System.Diagnostics;
using System.Runtime.CompilerServices;

#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.Intrinsics.X86.Ssse3;
#endif

namespace Snappier.Internal;

internal class CopyHelpers
{
#if NET8_0_OR_GREATER

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
        get
        {
            unsafe
            {
                // SAFETY: PshufbFillPatternsAsBytes is 128 bytes of static data, exactly 8 Vector128<byte> values,
                // and Vector128<byte> has no alignment requirement for reads.
                return MemoryMarshal.CreateReadOnlySpan(
                    reference: ref Unsafe.As<byte, Vector128<byte>>(ref MemoryMarshal.GetReference(PshufbFillPatternsAsBytes)),
                    length: 8);
            }
        }
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
    /// <safety>
    /// <paramref name="source"/>, <paramref name="op"/>, <paramref name="opEnd"/> and <paramref name="bufferEnd"/> must all
    /// reference the same buffer, with source &lt; op &lt;= opEnd &lt;= bufferEnd and opEnd - op &lt;= 64.
    /// The method may write past <paramref name="opEnd"/> but never at or past <paramref name="bufferEnd"/>.
    /// </safety>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void IncrementalCopy(ref readonly byte source, ref byte op, ref byte opEnd, ref byte bufferEnd)
    {
        DebugExtensions.Assert(Unsafe.IsAddressLessThan(in source, in op));
        DebugExtensions.Assert(!Unsafe.IsAddressGreaterThan(ref op, ref opEnd));
        DebugExtensions.Assert(!Unsafe.IsAddressGreaterThan(ref opEnd, ref bufferEnd));
        // NOTE: The copy tags use 3 or 6 bits to store the copy length, so len <= 64.
        DebugExtensions.Assert(Unsafe.ByteOffset(ref op, ref opEnd) <= (nint) 64);
        // NOTE: In practice the compressor always emits len >= 4, so it is ok to
        // assume that to optimize this function, but this is not guaranteed by the
        // compression format, so we have to also handle len < 4 in case the input
        // does not satisfy these conditions.

        int patternSize = (int) Unsafe.ByteOffset(in source, in op);

        if (patternSize < 8)
        {
#if NET8_0_OR_GREATER
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

                // SAFETY: bufferEnd - 16 is only compared with op, never dereferenced. If the buffer is shorter
                // than 16 bytes it points before the buffer, which the runtime does not formally allow even for a
                // comparison.
                if (!Unsafe.IsAddressGreaterThan(ref op, ref unsafe(Unsafe.Subtract(ref bufferEnd, 16))))
                {
                    Vector128<byte> shuffleMask = PshufbFillPatterns[patternSize];
                    // SAFETY: source < op <= bufferEnd - 16, so 16 bytes are readable at source.
                    Vector128<byte> srcPattern = unsafe(Vector128.LoadUnsafe(in source));
                    Vector128<byte> pattern = Shuffle(srcPattern, shuffleMask);

                    // Get the new pattern size now that we've repeated it
                    patternSize = PatternSizeTable[patternSize];

                    // If we're getting to the very end of the buffer, don't overrun
                    // SAFETY: op <= bufferEnd - 16 was checked above, so bufferEnd - 15 is inside the buffer.
                    ref byte loopEnd = ref unsafe(Unsafe.Subtract(ref bufferEnd, 15));
                    if (Unsafe.IsAddressGreaterThan(ref loopEnd, ref opEnd))
                    {
                        loopEnd = ref opEnd;
                    }

                    while (Unsafe.IsAddressLessThan(ref op, ref loopEnd))
                    {
                        unsafe
                        {
                            // SAFETY: op < loopEnd <= bufferEnd - 15, so 16 bytes are writable at op.
                            pattern.StoreUnsafe(ref op);
                            op = ref Unsafe.Add(ref op, patternSize);
                        }
                    }

                    if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
                    {
                        return;
                    }
                }

                unsafe
                {
                    // SAFETY: source < op <= opEnd still hold and share the caller's buffer.
                    IncrementalCopySlow(in source, ref op, ref opEnd);
                }
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
            // SAFETY: bufferEnd - 11 is only compared with op, never dereferenced. If the buffer is shorter
            // than 11 bytes it points before the buffer, which the runtime does not formally allow even for a
            // comparison.
            if (!Unsafe.IsAddressGreaterThan(ref op, ref unsafe(Unsafe.Subtract(ref bufferEnd, 11))))
                {
                    while (patternSize < 8)
                    {
                        unsafe
                        {
                            // SAFETY: the check above leaves the 11 writable bytes at op that this loop needs
                            // in the worst case, and source < op keeps every 8-byte read behind the writes.
                            UnalignedCopy64(in source, ref op);
                            op = ref Unsafe.Add(ref op, patternSize);
                        }
                        patternSize *= 2;
                    }

                    if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
                    {
                        return;
                    }
                }
                else
                {
                    unsafe
                    {
                        // SAFETY: source < op <= opEnd share the caller's buffer.
                        IncrementalCopySlow(in source, ref op, ref opEnd);
                    }
                    return;
                }
#if NET8_0_OR_GREATER
            }
#endif
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        DebugExtensions.Assert(patternSize >= 8);

        // Copy 2x 8 bytes at a time. Because op - src can be < 16, a single
        // UnalignedCopy128 might overwrite data in op. UnalignedCopy64 is safe
        // because expanding the pattern to at least 8 bytes guarantees that
        // op - src >= 8.
        //
        // Typically, the op_limit is the gating factor so try to simplify the loop
        // based on that.
        // SAFETY: bufferEnd - 16 is only compared with opEnd, never dereferenced. If the buffer is shorter
        // than 16 bytes it points before the buffer, which the runtime does not formally allow even for a
        // comparison.
        if (!Unsafe.IsAddressGreaterThan(ref opEnd, ref unsafe(Unsafe.Subtract(ref bufferEnd, 16))))
        {
            unsafe
            {
                // SAFETY: each pair of copies starts below opEnd and writes 16 bytes, and opEnd <= bufferEnd - 16.
                // Reads come from source, which is behind op in the same buffer.
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
            }

            return;
        }

        // Fall back to doing as much as we can with the available slop in the
        // buffer.

        unsafe
        {
            // SAFETY: op < bufferEnd - 16 on every iteration, so each 16-byte write stays in the buffer, and
            // source stays behind op. If the buffer is shorter than 16 bytes, loopEnd points before the buffer;
            // it is only compared, but the runtime does not formally allow that even for a comparison.
            for (ref byte loopEnd = ref Unsafe.Subtract(ref bufferEnd, 16);
                 Unsafe.IsAddressLessThan(ref op, ref loopEnd);
                 op = ref Unsafe.Add(ref op, 16), source = ref Unsafe.Add(in source, 16))
            {
                UnalignedCopy64(in source, ref op);
                UnalignedCopy64(in Unsafe.Add(in source, 8), ref Unsafe.Add(ref op, 8));
            }
        }

        if (!Unsafe.IsAddressLessThan(ref op, ref opEnd))
        {
            return;
        }

        // We only take this branch if we didn't have enough slop and we can do a
        // single 8 byte copy.
        unsafe
        {
            // SAFETY: the 8-byte copy only runs when op <= bufferEnd - 8, leaving 8 writable bytes at op. After it,
            // op can be past opEnd, which IncrementalCopySlow allows: it copies nothing in that case. source < op
            // still share the caller's buffer.
            if (!Unsafe.IsAddressGreaterThan(ref op, ref Unsafe.Subtract(ref bufferEnd, 8)))
            {
                UnalignedCopy64(in source, ref op);
                source = ref Unsafe.Add(in source, 8);
                op = ref Unsafe.Add(ref op, 8);
            }

            IncrementalCopySlow(in source, ref op, ref opEnd);
        }
    }

    /// <safety>
    /// <paramref name="source"/>, <paramref name="op"/> and <paramref name="opEnd"/> must reference the same buffer with
    /// source &lt; op. If op is at or past opEnd nothing is copied.
    /// </safety>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void IncrementalCopySlow(ref readonly byte source, ref byte op, ref byte opEnd)
    {
        while (Unsafe.IsAddressLessThan(ref op, ref opEnd))
        {
            op = source;

            unsafe
            {
                // SAFETY: the loop condition gives op < opEnd, so the next op is at most opEnd, and source stays behind
                // op in the same buffer.
                op = ref Unsafe.Add(ref op, 1);
                source = ref Unsafe.Add(in source, 1);
            }
        }
    }

    /// <safety>
    /// <paramref name="source"/> must have 8 readable bytes and <paramref name="destination"/> must have 8 writable bytes.
    /// Neither needs to be aligned on x86, x64 or ARM64. The copy uses ordinary typed loads and stores through
    /// Unsafe.As, so a misaligned address may fault on platforms that require aligned access, such as ARM32.
    /// </safety>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void UnalignedCopy64(ref readonly byte source, ref byte destination)
    {
        unsafe
        {
            // SAFETY: the caller guarantees 8 bytes at both source and destination.
            long tempStackVar = Unsafe.As<byte, long>(in source);
            Unsafe.As<byte, long>(ref destination) = tempStackVar;
        }
    }

    /// <safety>
    /// <paramref name="source"/> must have 16 readable bytes and <paramref name="destination"/> must have 16 writable bytes.
    /// Neither needs to be aligned on x86, x64 or ARM64. The copy uses ordinary typed loads and stores through
    /// Unsafe.As, so a misaligned address may fault on platforms that require aligned access, such as ARM32.
    /// </safety>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void UnalignedCopy128(ref readonly byte source, ref byte destination)
    {
        unsafe
        {
            // SAFETY: the caller guarantees 16 bytes at both source and destination.
            Guid tempStackVar = Unsafe.As<byte, Guid>(in source);
            Unsafe.As<byte, Guid>(ref destination) = tempStackVar;
        }
    }
}
