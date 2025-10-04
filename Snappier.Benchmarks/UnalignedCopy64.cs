using System.Runtime.CompilerServices;
using BenchmarkDotNet.Diagnostics.Windows.Configs;

namespace Snappier.Benchmarks;

[RyuJitX64Job]
[InliningDiagnoser(false, ["Snappier.Benchmarks"])]
public class UnalignedCopy64
{
    private readonly byte[] _buffer = new byte[16];

    [Benchmark]
    public void Default()
    {
        ref byte ptr = ref _buffer[0];
        CopyHelpers.UnalignedCopy64(in ptr, ref Unsafe.Add(ref ptr, 8));
    }
}
