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
            fixed (byte* s1 = _array)
            {
                var s2 = s1 + 12;
                var s2Limit = s1 + _array.Length;

                return SnappyCompressor.FindMatchLength(s1, s2, s2Limit, ref data);
            }
        }
    }
}
