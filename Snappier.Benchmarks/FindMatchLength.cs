using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class FindMatchLength
    {
        private static readonly byte[] s_fourByteMatch =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            1, 2, 3, 4, 4, 6, 7, 8, 9, 10, 11, 13,
            // Padding so we ensure we're hitting the hot (and fast) path where there is plenty more data in the input buffer
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };

        private static readonly byte[] s_sevenByteMatch =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            1, 2, 3, 4, 5, 6, 7, 7, 9, 10, 11, 13,
            // Padding so we ensure we're hitting the hot (and fast) path where there is plenty more data in the input buffer
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };

        private static readonly byte[] s_elevenByteMatch =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13,
            // Padding so we ensure we're hitting the hot (and fast) path where there is plenty more data in the input buffer
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };

        private static readonly byte[] s_thirtyTwoByteMatch =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            // Padding so we ensure we're hitting the hot (and fast) path where there is plenty more data in the input buffer
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };

        [Params(4, 7, 11, 32)]
        public int MatchLength { get; set; }

        private byte[] _array;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _array = MatchLength switch
            {
                4 => s_fourByteMatch,
                7 => s_sevenByteMatch,
                11 => s_elevenByteMatch,
                32 => s_thirtyTwoByteMatch,
                _ => throw new InvalidOperationException()
            };
        }

        [Benchmark(Baseline = true)]
        public (long, bool) Regular()
        {
            ulong data = 0;

            ref byte s1 = ref _array[0];
            ref byte s2 = ref Unsafe.Add(ref s1, 12);
            ref byte s2Limit = ref Unsafe.Add(ref s1, _array.Length);

            return SnappyCompressor.FindMatchLength(ref s1, ref s2, ref s2Limit, ref data);
        }
    }
}
