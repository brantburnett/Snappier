using System.Runtime.CompilerServices;

namespace Snappier.Benchmarks;

public class IncrementalCopy
{
    private readonly byte[] _buffer = new byte[128];

    [Benchmark]
    public void Fast()
    {
        unsafe
        {
            // SAFETY: all references are within the 128-byte _buffer.
            ref byte buffer = ref _buffer[0];
            CopyHelpers.IncrementalCopy(in buffer, ref Unsafe.Add(ref buffer, 2), ref Unsafe.Add(ref buffer, 18),
                ref Unsafe.Add(ref buffer, _buffer.Length - 1));
        }
    }

    [Benchmark(Baseline = true)]
    public void SlowCopyMemory()
    {
        unsafe
        {
            // SAFETY: all references are within the 128-byte _buffer.
            ref byte buffer = ref _buffer[0];
            CopyHelpers.IncrementalCopySlow(in buffer, ref Unsafe.Add(ref buffer, 2), ref Unsafe.Add(ref buffer, 18));
        }
    }
}
