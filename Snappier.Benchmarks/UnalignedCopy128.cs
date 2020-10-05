using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [RyuJitX64Job]
    [InliningDiagnoser(false, new[] {"Snappier.Benchmarks"})]
    public class UnalignedCopy64
    {
        private readonly byte[] _buffer = new byte[16];

        [Benchmark]
        public unsafe void Default()
        {
            fixed (byte* ptr = _buffer)
            {
                CopyHelpers.UnalignedCopy64(ptr, ptr + 8);
            }
        }
    }
}
