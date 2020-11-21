using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
#if NETCOREAPP3_0 || NET5_0
using System.Numerics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Snappier.Internal
{
    internal static class Helpers
    {
        private const uint HashMultiplier = 0x1e35a7bd;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HashBytes(uint bytes, int shift)
        {
            unchecked
            {
                return (int)((bytes * HashMultiplier) >> shift);
            }
        }

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

            return 32 + sourceBytes + sourceBytes / 6;
        }

        private static readonly byte[] LeftShiftOverflowsMasks =
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x80, 0xc0, 0xe0, 0xf0, 0xf8, 0xfc, 0xfe
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LeftShiftOverflows(byte value, int shift) =>
            (value & LeftShiftOverflowsMasks[shift]) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ExtractLowBytes(uint value, int numBytes)
        {
            Debug.Assert(numBytes >= 0);
            Debug.Assert(numBytes <= 4);

            #if NETCOREAPP3_0 || NET5_0
            if (Bmi2.IsSupported)
            {
                return Bmi2.ZeroHighBits(value, (uint)(numBytes * 8));
            }
            else
            {
                return value & ~(0xffffffff << (8 * numBytes));
            }
            #else
            return value & ~(0xffffffff << (8 * numBytes));
            #endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int UnsafeReadInt32(void* ptr)
        {
            var result = Unsafe.ReadUnaligned<int>(ptr);
            if (!BitConverter.IsLittleEndian)
            {
                result = BinaryPrimitives.ReverseEndianness(result);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint UnsafeReadUInt32(void* ptr)
        {
            var result = Unsafe.ReadUnaligned<uint>(ptr);
            if (!BitConverter.IsLittleEndian)
            {
                result = BinaryPrimitives.ReverseEndianness(result);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ulong UnsafeReadUInt64(void* ptr)
        {
            var result = Unsafe.ReadUnaligned<ulong>(ptr);
            if (!BitConverter.IsLittleEndian)
            {
                result = BinaryPrimitives.ReverseEndianness(result);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void UnsafeWriteUInt32(void* ptr, uint value)
        {
            if (!BitConverter.IsLittleEndian)
            {
                value = BinaryPrimitives.ReverseEndianness(value);
            }

            Unsafe.WriteUnaligned(ptr, value);
        }

        /// <summary>
        /// Return floor(log2(n)) for positive integer n.  Returns -1 iff n == 0.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2Floor(uint n) =>
            n == 0 ? -1 : Log2FloorNonZero(n);


        /// <summary>
        /// Return floor(log2(n)) for positive integer n.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2FloorNonZero(uint n)
        {
            Debug.Assert(n != 0);

#if NETCOREAPP3_0 || NET5_0
            return BitOperations.Log2(n);
#else
            return (int) Math.Floor(Math.Log(n, 2));
#endif
        }

        /// <summary>
        /// Finds the index of the least significant non-zero bit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindLsbSetNonZero(uint n)
        {
            Debug.Assert(n != 0);

#if NETCOREAPP3_0 || NET5_0
            return BitOperations.TrailingZeroCount(n);
#else
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
#endif
        }
    }
}
