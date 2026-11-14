using System.Runtime.CompilerServices;
using BenchmarkDotNet.Diagnostics.Windows.Configs;

namespace Snappier.Benchmarks;

[RyuJitX64Job]
[InliningDiagnoser(false, ["Snappier.Benchmarks"])]
public class UnalignedCopy128
{
    private readonly byte[] _buffer = new byte[32];

    [Benchmark]
    public void Default()
    {
        unsafe
        {
            // SAFETY: _buffer is 32 bytes, so both 16-byte halves are in bounds.
            ref byte ptr = ref _buffer[0];
            CopyHelpers.UnalignedCopy128(in ptr, ref Unsafe.Add(ref ptr, 16));
        }
    }
}
