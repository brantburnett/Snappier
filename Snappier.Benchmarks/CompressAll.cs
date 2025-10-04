using System.IO.Compression;

namespace Snappier.Benchmarks;

public class CompressAll
{
    private MemoryStream _source;
    private MemoryStream _destination;

    [Params("alice29.txt", "asyoulik.txt", "fireworks.jpeg", "geo.protodata", "html", "html_x_4",
        "kppkn.gtb", "lcet10.txt", "paper-100k.pdf", "plrabn12.txt", "urls.10K")]
    public string FileName;

    [GlobalSetup]
    public void LoadToMemory()
    {
        _source = new MemoryStream();
        _destination = new MemoryStream();

        using Stream resource =
            typeof(CompressAll).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData." + FileName);

        // ReSharper disable once PossibleNullReferenceException
        resource.CopyTo(_source);
    }

    [Benchmark]
    public void Compress()
    {
        _source.Position = 0;
        _destination.Position = 0;
        using var stream = new SnappyStream(_destination, CompressionMode.Compress, true);

        _source.CopyTo(stream, 65536);
        stream.Flush();
    }
}
