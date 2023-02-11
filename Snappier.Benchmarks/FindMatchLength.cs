using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class FindMatchLength
    {
        private readonly byte[] _array =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13,
        };

        [Benchmark(Baseline = true)]
        public unsafe (long, bool) Regular()
        {
            ulong data = 0;

            ref byte s1 = ref _array[0];
            ref byte s2 = ref Unsafe.Add(ref s1, 12);
            ref byte s2Limit = ref Unsafe.Add(ref s1, _array.Length - 1);

            return SnappyCompressor.FindMatchLength(ref s1, ref s2, ref s2Limit, ref data);
        }
    }
}
