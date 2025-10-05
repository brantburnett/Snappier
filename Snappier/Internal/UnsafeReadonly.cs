using System.Runtime.CompilerServices;

namespace Snappier.Internal;

// Helpers to perform Unsafe.Add operations on readonly refs.
internal static class UnsafeReadonly
{
    extension(Unsafe)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Add<T>(ref readonly T source, int elementOffset) =>
            ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Add<T>(ref readonly T source, nint elementOffset) =>
            ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Add<T>(ref readonly T source, nuint elementOffset) =>
            ref Unsafe.Add(ref Unsafe.AsRef(in source), elementOffset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly TTo As<TFrom, TTo>(ref readonly TFrom source) =>
            ref Unsafe.As<TFrom, TTo>(ref Unsafe.AsRef(in source));

#if NETSTANDARD2_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint ByteOffset<T>(ref readonly T origin, ref readonly T target) =>
            Unsafe.ByteOffset(ref Unsafe.AsRef(in origin), ref Unsafe.AsRef(in target));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyBlockUnaligned(ref byte destination, ref readonly byte source, uint byteCount) =>
            Unsafe.CopyBlockUnaligned(ref destination, ref Unsafe.AsRef(in source), byteCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressGreaterThan<T>(ref readonly T left, ref readonly T right) =>
            Unsafe.IsAddressGreaterThan(ref Unsafe.AsRef(in left), ref Unsafe.AsRef(in right));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAddressLessThan<T>(ref readonly T left, ref readonly T right) =>
            Unsafe.IsAddressLessThan(ref Unsafe.AsRef(in left), ref Unsafe.AsRef(in right));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadUnaligned<T>(ref readonly byte source) =>
            Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in source));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Subtract<T>(ref readonly T source, int elementOffset) =>
            ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Subtract<T>(ref readonly T source, nint elementOffset) =>
            ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T Subtract<T>(ref readonly T source, nuint elementOffset) =>
            ref Unsafe.Subtract(ref Unsafe.AsRef(in source), elementOffset);
    }
}
