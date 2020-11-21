using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET5_0
using System.Runtime.Intrinsics.Arm;
#endif
#if NETCOREAPP3_0 || NET5_0
using System.Runtime.Intrinsics.X86;
#endif

namespace Snappier.Internal
{
    internal static class Crc32CAlgorithm
    {
        #region static

        private const uint Poly = 0x82F63B78u;

        private static readonly uint[] Table;

        static Crc32CAlgorithm()
        {
            var table = new uint[16 * 256];
            for (uint i = 0; i < 256; i++)
            {
                uint res = i;
                for (int t = 0; t < 16; t++)
                {
                    for (int k = 0; k < 8; k++) res = (res & 1) == 1 ? Poly ^ (res >> 1) : (res >> 1);
                    table[(t * 256) + i] = res;
                }
            }

            Table = table;
        }

        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Compute(ReadOnlySpan<byte> source)
        {
            return Append(0, source);
        }

        public static uint Append(uint crc, ReadOnlySpan<byte> source)
        {
            uint crcLocal = uint.MaxValue ^ crc;

            #if NET5_0
            // If available on the current CPU, use ARM CRC32C intrinsic operations.
            // The if Crc32 statements are optimized out by the JIT compiler based on CPU support.
            if (Crc32.IsSupported)
            {
                if (Crc32.Arm64.IsSupported)
                {
                    while (source.Length >= 8)
                    {
                        crcLocal = Crc32.Arm64.ComputeCrc32C(crcLocal, MemoryMarshal.Read<ulong>(source));
                        source = source.Slice(8);
                    }
                }

                // Process in 4-byte chunks
                while (source.Length >= 4)
                {
                    crcLocal = Crc32.ComputeCrc32C(crcLocal, MemoryMarshal.Read<uint>(source));
                    source = source.Slice(4);
                }

                // Process the remainder
                int j = 0;
                while (j < source.Length)
                {
                    crcLocal = Crc32.ComputeCrc32C(crcLocal, source[j++]);
                }

                return crcLocal ^ uint.MaxValue;
            }
            #endif

            #if NETCOREAPP3_0 || NET5_0
            // If available on the current CPU, use Intel CRC32C intrinsic operations.
            // The Sse42 if statements are optimized out by the JIT compiler based on CPU support.
            if (Sse42.IsSupported)
            {
                // Process in 8-byte chunks first if 64-bit
                if (Sse42.X64.IsSupported)
                {
                    if (source.Length >= 8)
                    {
                        // work with a ulong local during the loop to reduce typecasts
                        ulong crcLocalLong = crcLocal;

                        while (source.Length >= 8)
                        {
                            crcLocalLong = Sse42.X64.Crc32(crcLocalLong, MemoryMarshal.Read<ulong>(source));
                            source = source.Slice(8);
                        }

                        crcLocal = (uint) crcLocalLong;
                    }
                }

                // Process in 4-byte chunks
                while (source.Length >= 4)
                {
                    crcLocal = Sse42.Crc32(crcLocal, MemoryMarshal.Read<uint>(source));
                    source = source.Slice(4);
                }

                // Process the remainder
                int j = 0;
                while (j < source.Length)
                {
                    crcLocal = Sse42.Crc32(crcLocal, source[j++]);
                }

                return crcLocal ^ uint.MaxValue;
            }
            #endif

            uint[] table = Table;
            while (source.Length >= 16)
            {
                var a = table[(3 * 256) + source[12]]
                        ^ table[(2 * 256) + source[13]]
                        ^ table[(1 * 256) + source[14]]
                        ^ table[(0 * 256) + source[15]];

                var b = table[(7 * 256) + source[8]]
                        ^ table[(6 * 256) + source[9]]
                        ^ table[(5 * 256) + source[10]]
                        ^ table[(4 * 256) + source[11]];

                var c = table[(11 * 256) + source[4]]
                        ^ table[(10 * 256) + source[5]]
                        ^ table[(9 * 256) + source[6]]
                        ^ table[(8 * 256) + source[7]];

                var d = table[(15 * 256) + ((byte)crcLocal ^ source[0])]
                        ^ table[(14 * 256) + ((byte)(crcLocal >> 8) ^ source[1])]
                        ^ table[(13 * 256) + ((byte)(crcLocal >> 16) ^ source[2])]
                        ^ table[(12 * 256) + ((crcLocal >> 24) ^ source[3])];

                crcLocal = d ^ c ^ b ^ a;
                source = source.Slice(16);
            }

            for (int offset = 0; offset < source.Length; offset++)
            {
                crcLocal = table[(byte) (crcLocal ^ source[offset])] ^ crcLocal >> 8;
            }

            return crcLocal ^ uint.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ApplyMask(uint x) =>
            unchecked(((x >> 15) | (x << 17)) + 0xa282ead8);
    }
}
