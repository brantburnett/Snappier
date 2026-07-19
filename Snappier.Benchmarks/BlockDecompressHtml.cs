namespace Snappier.Benchmarks;

[MemoryDiagnoser]
public class BlockDecompressHtml
{
    private ReadOnlyMemory<byte> _input;
    private SnappyDecompressor _decompressor = null!;

    [GlobalSetup]
    public void LoadToMemory()
    {
        using Stream resource =
            typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

        byte[] input = new byte[65536]; // Just test the first 64KB
        // ReSharper disable once PossibleNullReferenceException
        int inputLength = resource!.Read(input, 0, input.Length);

        byte[] compressed = new byte[Snappy.GetMaxCompressedLength(inputLength)];
        int compressedLength = Snappy.Compress(input.AsSpan(0, inputLength), compressed);

        _input = compressed.AsMemory(0, compressedLength);
        _decompressor = new SnappyDecompressor();
    }

    [Benchmark(Baseline = true)]
    public void Decompress()
    {
        using var decompressor = new SnappyDecompressor();

        decompressor.Decompress(_input.Span);
    }

    [Benchmark]
    public void DecompressReusable()
    {
        _decompressor.Reset();
        _decompressor.Decompress(_input.Span);
    }

    [GlobalCleanup]
    public void Cleanup() => _decompressor.Dispose();
}
