using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if !NETSTANDARD2_0
using System.Numerics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Snappier.Internal;

internal static class Helpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MaxCompressedLength(int sourceBytes)
    {
        // Compressed data can be defined as:
        //    compressed := item* literal*
        //    item       := literal* copy
        //
        // The trailing literal sequence has a space blowup of at most 62/60
        // since a literal of length 60 needs one tag byte + one extra byte
        // for length information.
        //
        // We also add one extra byte to the blowup to account for the use of
        // "ref byte" pointers. The output index will be pushed one byte past
        // the end of the output data, but for safety we need to ensure that
        // it still points to an element in the buffer array.
        //
        // Item blowup is trickier to measure.  Suppose the "copy" op copies
        // 4 bytes of data.  Because of a special check in the encoding code,
        // we produce a 4-byte copy only if the offset is < 65536.  Therefore
        // the copy op takes 3 bytes to encode, and this type of item leads
        // to at most the 62/60 blowup for representing literals.
        //
        // Suppose the "copy" op copies 5 bytes of data.  If the offset is big
        // enough, it will take 5 bytes to encode the copy op.  Therefore the
        // worst case here is a one-byte literal followed by a five-byte copy.
        // I.e., 6 bytes of input turn into 7 bytes of "compressed" data.
        //
        // This last factor dominates the blowup, so the final estimate is:

        return 32 + sourceBytes + sourceBytes / 6 + 1;
    }

    // Constant for MaxCompressedLength when passed Constants.BlockSize, keep this in sync with the above method
    public const int MaxBlockCompressedLength = (int)(32 + Constants.BlockSize + Constants.BlockSize / 6 + 1);

    /// <summary>
    /// Clears the array and returns it to the pool, clearing only the used portion of the array.
    /// This is a minor performance optimization to avoid clearing the entire array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearAndReturn(byte[] array, int usedLength)
    {
        Debug.Assert(array is not null);
        Debug.Assert(usedLength >= 0);

        array.AsSpan(0, usedLength).Clear();
        ArrayPool<byte>.Shared.Return(array);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool LeftShiftOverflows(byte value, int shift)
    {
        Debug.Assert(shift < 32);
        return (value & ~(0xffff_ffffu >>> shift)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ExtractLowBytes(uint value, int numBytes)
    {
        Debug.Assert(numBytes >= 0);
        Debug.Assert(numBytes <= 4);

#if !NETSTANDARD2_0
        if (Bmi2.IsSupported)
        {
            return Bmi2.ZeroHighBits(value, (uint)(numBytes * 8));
        }
#endif

        return value & ~(0xffffffff << (8 * numBytes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint UnsafeReadUInt32(ref readonly byte ptr)
    {
        uint result = Unsafe.ReadUnaligned<uint>(in ptr);

        if (!BitConverter.IsLittleEndian)
        {
            result = BinaryPrimitives.ReverseEndianness(result);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong UnsafeReadUInt64(ref readonly byte ptr)
    {
        ulong result = Unsafe.ReadUnaligned<ulong>(in ptr);

        if (!BitConverter.IsLittleEndian)
        {
            result = BinaryPrimitives.ReverseEndianness(result);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnsafeWriteUInt32(ref byte ptr, uint value)
    {
        if (!BitConverter.IsLittleEndian)
        {
            value = BinaryPrimitives.ReverseEndianness(value);
        }

        Unsafe.WriteUnaligned(ref ptr, value);
    }

#if NETSTANDARD2_0

    // Port from .NET 7 BitOperations of a faster fallback algorithm for .NET Standard since we don't have intrinsics
    // or BitOperations. This is the same algorithm used by BitOperations.Log2 when hardware acceleration is unavailable.
    // https://github.com/dotnet/runtime/blob/bee217ffbdd6b3ad60b0e1e17c6370f4bb618779/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs#L404

    private static ReadOnlySpan<byte> Log2DeBruijn =>
    [
        00, 09, 01, 10, 13, 21, 02, 29,
        11, 14, 16, 18, 22, 25, 03, 30,
        08, 12, 20, 28, 15, 17, 24, 07,
        19, 27, 23, 06, 26, 05, 04, 31
    ];

    /// <summary>
    /// Returns the integer (floor) log of the specified value, base 2.
    /// Note that by convention, input value 0 returns 0 since Log(0) is undefined.
    /// Does not directly use any hardware intrinsics, nor does it incur branching.
    /// </summary>
    /// <param name="value">The value.</param>
    private static int Log2SoftwareFallback(uint value)
    {
        // No AggressiveInlining due to large method size
        // Has conventional contract 0->0 (Log(0) is undefined)

        // Fill trailing zeros with ones, eg 00010010 becomes 00011111
        value |= value >> 01;
        value |= value >> 02;
        value |= value >> 04;
        value |= value >> 08;
        value |= value >> 16;

        // uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
        return Unsafe.AddByteOffset(
            // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
            ref MemoryMarshal.GetReference(Log2DeBruijn),
            // uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
            (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
    }

#endif

    /// <summary>
    /// Return floor(log2(n)) for positive integer n. Returns 0 for the special case n = 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Log2Floor(uint n)
    {
#if NETSTANDARD2_0
        return Log2SoftwareFallback(n);
#else
        return BitOperations.Log2(n);
#endif
    }

    /// <summary>
    /// Finds the index of the least significant non-zero bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindLsbSetNonZero(uint n)
    {
        Debug.Assert(n != 0);

#if NETSTANDARD2_0
        int rc = 31;
        int shift = 1 << 4;

        for (int i = 4; i >= 0; --i)
        {
            uint x = n << shift;
            if (x != 0)
            {
                n = x;
                rc -= shift;
            }

            shift >>= 1;
        }

        return rc;
#else
        return BitOperations.TrailingZeroCount(n);
#endif
    }

    /// <summary>
    /// Finds the index of the least significant non-zero bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindLsbSetNonZero(ulong n)
    {
        Debug.Assert(n != 0);

#if NETSTANDARD2_0
        uint bottomBits = unchecked((uint)n);
        if (bottomBits == 0)
        {
            return 32 + FindLsbSetNonZero(unchecked((uint)(n >> 32)));
        }
        else
        {
            return FindLsbSetNonZero(bottomBits);
        }
#else
        return BitOperations.TrailingZeroCount(n);
#endif
    }
}
