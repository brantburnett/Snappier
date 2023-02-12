using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class IncrementalCopy
    {
        private readonly byte[] _buffer = new byte[128];

        [Benchmark]
        public void Fast()
        {
            ref byte buffer = ref _buffer[0];
            CopyHelpers.IncrementalCopy(ref buffer, ref Unsafe.Add(ref buffer, 2), ref Unsafe.Add(ref buffer, 18),
                ref Unsafe.Add(ref buffer, _buffer.Length - 1));
        }

        [Benchmark(Baseline = true)]
        public void SlowCopyMemory()
        {
            ref byte buffer = ref _buffer[0];
            CopyHelpers.IncrementalCopySlow(ref buffer, ref Unsafe.Add(ref buffer, 2), ref Unsafe.Add(ref buffer, 18));
        }
    }
}
