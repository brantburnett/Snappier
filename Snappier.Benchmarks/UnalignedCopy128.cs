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
        ref byte ptr = ref _buffer[0];
        CopyHelpers.UnalignedCopy128(in ptr, ref Unsafe.Add(ref ptr,16));
    }
}
