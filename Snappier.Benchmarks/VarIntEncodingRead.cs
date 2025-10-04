namespace Snappier.Benchmarks;

public class VarIntEncodingRead
{
    [Params(0u, 256u, 65536u)]
    public uint Value { get; set; }

    readonly byte[] _source = new byte[16];

    [GlobalSetup]
    public void GlobalSetup()
    {
        VarIntEncoding.Write(_source, Value);
    }

    [Benchmark]
    public (int, uint) TryRead()
    {
        _ = VarIntEncoding.TryRead(_source, out uint result, out int length);

        return (length, result);
    }
}
