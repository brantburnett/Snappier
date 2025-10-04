namespace Snappier.Benchmarks;

public class BlockCompressHtml
{
    private ReadOnlyMemory<byte> _input;
    private Memory<byte> _output;

    [GlobalSetup]
    public void LoadToMemory()
    {
        using Stream resource =
            typeof(BlockCompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

        byte[] input = new byte[65536]; // Just test the first 64KB
        int inputLength = resource!.Read(input, 0, input.Length);
        _input = input.AsMemory(0, inputLength);

        _output = new byte[Snappy.GetMaxCompressedLength(inputLength)];
    }

    [Benchmark]
    public int Compress()
    {
        using var compressor = new SnappyCompressor();

#pragma warning disable CS0618 // Type or member is obsolete
        return compressor.Compress(_input.Span, _output.Span);
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
