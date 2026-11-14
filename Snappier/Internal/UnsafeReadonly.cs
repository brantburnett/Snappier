using System.Runtime.CompilerServices;

namespace Snappier.Internal;

// Helpers to perform Unsafe.Add operations on readonly refs.
//
// Every member that produces a reference, reads or copies is requires-unsafe and has the same contract as the
// System.Runtime.CompilerServices.Unsafe member it forwards to, stated in its <safety> comment. ByteOffset,
// IsAddressGreaterThan and IsAddressLessThan only compare addresses, so they are safe, as they are in the
// .NET 11 BCL.
internal static class UnsafeReadonly
{
    extension(Unsafe)
    {
        /// <safety>
        /// <paramref name="source"/> offset by <paramref name="elementOffset"/> elements must stay within the same
        /// object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Add<T>(ref readonly T source, int elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);
            }
        }

        /// <safety>
        /// <paramref name="source"/> offset by <paramref name="elementOffset"/> elements must stay within the same
        /// object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Add<T>(ref readonly T source, nint elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);
            }
        }

        /// <safety>
        /// <paramref name="source"/> offset by <paramref name="elementOffset"/> elements must stay within the same
        /// object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Add<T>(ref readonly T source, nuint elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);
            }
        }

#if !NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint ByteOffset<T>(ref readonly T origin, ref readonly T target)
        {
            unsafe
            {
                // SAFETY: AsRef only drops readonly so the addresses can be compared; nothing is read or written.
                return Unsafe.ByteOffset(ref Unsafe.AsRef(in origin), ref Unsafe.AsRef(in target));
            }
        }

        /// <safety>
        /// <paramref name="source"/> must have <paramref name="byteCount"/> readable bytes and
        /// <paramref name="destination"/> must have <paramref name="byteCount"/> writable bytes. The two ranges must not
        /// overlap. If they do, the copy stays within those bytes, but the bytes copied are unspecified.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CopyBlockUnaligned(ref byte destination, ref readonly byte source, uint byteCount)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                Unsafe.CopyBlockUnaligned(ref destination, ref Unsafe.AsRef(in source), byteCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressGreaterThan<T>(ref readonly T left, ref readonly T right)
        {
            unsafe
            {
                // SAFETY: AsRef only drops readonly so the addresses can be compared; nothing is read or written.
                return Unsafe.IsAddressGreaterThan(ref Unsafe.AsRef(in left), ref Unsafe.AsRef(in right));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressLessThan<T>(ref readonly T left, ref readonly T right)
        {
            unsafe
            {
                // SAFETY: AsRef only drops readonly so the addresses can be compared; nothing is read or written.
                return Unsafe.IsAddressLessThan(ref Unsafe.AsRef(in left), ref Unsafe.AsRef(in right));
            }
        }

        /// <safety>
        /// <paramref name="source"/> must have sizeof(<typeparamref name="T"/>) readable bytes, and
        /// <typeparamref name="T"/> must be valid for any bit pattern stored there.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T ReadUnaligned<T>(ref readonly byte source)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in source));
            }
        }
#endif

        /// <safety>
        /// <paramref name="source"/> offset back by <paramref name="elementOffset"/> elements must stay within the
        /// same object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Subtract<T>(ref readonly T source, int elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);
            }
        }

        /// <safety>
        /// <paramref name="source"/> offset back by <paramref name="elementOffset"/> elements must stay within the
        /// same object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Subtract<T>(ref readonly T source, nint elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);
            }
        }

        /// <safety>
        /// <paramref name="source"/> offset back by <paramref name="elementOffset"/> elements must stay within the
        /// same object or buffer, and any read through the result must not exceed its bounds.
        /// </safety>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T Subtract<T>(ref readonly T source, nuint elementOffset)
        {
            unsafe
            {
                // SAFETY: forwards the caller's obligation stated in the <safety> comment.
                return ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);
            }
        }
    }
}
