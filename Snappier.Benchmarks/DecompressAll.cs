using System.IO.Compression;

namespace Snappier.Benchmarks;

public class DecompressAll
{
    private MemoryStream _memoryStream;
    private byte[] _buffer;

    [Params("alice29.txt", "asyoulik.txt", "fireworks.jpeg", "geo.protodata", "html", "html_x_4",
        "kppkn.gtb", "lcet10.txt", "paper-100k.pdf", "plrabn12.txt", "urls.10K")]
    public string FileName;

    [GlobalSetup]
    public void LoadToMemory()
    {
        _memoryStream = new MemoryStream();

        using Stream resource =
            typeof(DecompressAll).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData." + FileName);

        using var compressStream = new SnappyStream(_memoryStream, CompressionMode.Compress, true);

        // ReSharper disable once PossibleNullReferenceException
        resource.CopyTo(compressStream);
        compressStream.Flush();

        _buffer = new byte[65536];
    }


    [Benchmark]
    public void Decompress()
    {
        _memoryStream.Position = 0;
        using var stream = new SnappyStream(_memoryStream, CompressionMode.Decompress, true);

        while (stream.Read(_buffer, 0, _buffer.Length) > 0)
        {
        }
    }
}
