using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [RyuJitX64Job]
    [InliningDiagnoser(false, new[] {"Snappier.Benchmarks"})]
    public class UnsafeReadInt32
    {
        private readonly byte[] _buffer = new byte[4];

        [Benchmark]
        public unsafe int Default()
        {
            fixed (byte* ptr = _buffer)
            {
                return Helpers.UnsafeReadInt32(ptr);
            }
        }
    }
}
