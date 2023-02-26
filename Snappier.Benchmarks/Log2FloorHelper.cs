using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [DisassemblyDiagnoser(2)]
    public class Log2FloorHelper
    {
        private uint _n = 5;

        [Benchmark]
        public int Log2Floor()
        {
            return Helpers.Log2Floor(_n);
        }
    }
}
