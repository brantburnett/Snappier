using System.Buffers;

namespace Snappier.Benchmarks;

[MemoryDiagnoser]
public class BlockDecompressHtml
{
    private ReadOnlyMemory<byte> _input;
    private Memory<byte> _output;
    private SnappyBlockDecoder _decoder = null!;

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
        _output = new byte[inputLength];
        _decoder = new SnappyBlockDecoder();
    }

    [Benchmark(Baseline = true)]
    public int Decompress() => Snappy.Decompress(_input.Span, _output.Span);

    [Benchmark]
    public OperationStatus DecompressReusable()
    {
        _decoder.Reset();
        return _decoder.Decompress(_input.Span, _output.Span, out _, out _);
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder.Dispose();
}
