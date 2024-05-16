using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class LeftShiftOverflows
    {
        private byte value = 24;
        private int shift = 7;

        [Benchmark(Baseline = true)]
        public bool Current()
        {
            return Helpers.LeftShiftOverflows(value, shift);
        }
    }
}
