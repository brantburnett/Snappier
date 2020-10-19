using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [SimpleJob]
    public class IncrementalCopy
    {
        private readonly byte[] _buffer = new byte[128];

        [Benchmark]
        public unsafe void Fast()
        {
            fixed (byte* buffer = _buffer)
            {
                fixed (sbyte* pshufbFillPatterns = CopyHelpers.PshufbFillPatterns)
                {
                    CopyHelpers.IncrementalCopy(buffer, buffer + 2, buffer + 18, buffer + _buffer.Length, pshufbFillPatterns);
                }
            }
        }

        [Benchmark(Baseline = true)]
        public unsafe void SlowCopyMemory()
        {
            fixed (byte* buffer = _buffer)
            {
                CopyHelpers.IncrementalCopySlow(buffer, buffer + 2, buffer + 18);
            }
        }
    }
}
