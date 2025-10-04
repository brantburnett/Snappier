namespace Snappier.Benchmarks;

public class VarIntEncodingWrite
{
    [Params(0u, 256u, 65536u)]
    public uint Value { get; set; }

    readonly byte[] _dest = new byte[8];

    [Benchmark(Baseline = true)]
    public int Baseline()
    {
        return VarIntEncoding.Write(_dest, Value);
    }
}
