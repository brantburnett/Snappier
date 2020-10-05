using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Snappier.Internal
{
    internal sealed class Crc32CAlgorithm
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

        private uint _currentCrc;

		/// <summary>
		/// Initializes a new instance of the <see cref="Crc32CAlgorithm"/> class.
		/// </summary>
		public Crc32CAlgorithm()
		{
		}

		public void Initialize()
		{
			_currentCrc = 0;
		}

        public uint ComputeHash(ReadOnlySpan<byte> source)
        {
            HashCore(source);
            var result = HashFinal();

            Initialize();

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HashCore(ReadOnlySpan<byte> source)
        {
            uint crcLocal = uint.MaxValue ^ _currentCrc;

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

            _currentCrc = crcLocal ^ uint.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint HashFinal()
        {
            Span<byte> destination = stackalloc byte[4];

            unchecked
            {
                destination[0] = (byte) _currentCrc;
                destination[1] = (byte) (_currentCrc >> 8);
                destination[2] = (byte) (_currentCrc >> 16);
                destination[3] = (byte) (_currentCrc >> 24);
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(destination);
        }

        public static uint ApplyMask(uint x) =>
            unchecked(((x >> 15) | (x << 17)) + 0xa282ead8);
    }
}
