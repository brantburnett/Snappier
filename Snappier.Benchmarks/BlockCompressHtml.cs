namespace Snappier.Benchmarks;

[MemoryDiagnoser]
public class BlockCompressHtml
{
    private ReadOnlyMemory<byte> _input;
    private Memory<byte> _output;
    private SnappyBlockEncoder _encoder = null!;

    [GlobalSetup]
    public void LoadToMemory()
    {
        using Stream resource =
            typeof(BlockCompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

        byte[] input = new byte[65536]; // Just test the first 64KB
        int inputLength = resource!.Read(input, 0, input.Length);
        _input = input.AsMemory(0, inputLength);

        _output = new byte[Snappy.GetMaxCompressedLength(inputLength)];
        _encoder = new SnappyBlockEncoder();
    }

    [Benchmark(Baseline = true)]
    public int Compress() => Snappy.Compress(_input.Span, _output.Span);

    [Benchmark]
    public int CompressReusable() => _encoder.Compress(_input.Span, _output.Span);

    [GlobalCleanup]
    public void Cleanup() => _encoder.Dispose();
}
