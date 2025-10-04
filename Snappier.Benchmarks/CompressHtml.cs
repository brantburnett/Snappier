using System.IO.Compression;

namespace Snappier.Benchmarks;

public class CompressHtml
{
    private MemoryStream _source;
    private MemoryStream _destination;

    [Params(16384)]
    public int ReadSize;

    [GlobalSetup]
    public void LoadToMemory()
    {
        _source = new MemoryStream();
        _destination = new MemoryStream();

        using Stream resource =
            typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html_x_4");

        // ReSharper disable once PossibleNullReferenceException
        resource.CopyTo(_source);
    }

    [Benchmark]
    public void Compress()
    {
        _source.Position = 0;
        _destination.Position = 0;
        using var stream = new SnappyStream(_destination, CompressionMode.Compress, true);

        _source.CopyTo(stream, ReadSize);
        stream.Flush();
    }
}
