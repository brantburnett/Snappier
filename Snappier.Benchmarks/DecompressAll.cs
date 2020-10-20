using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    //[ShortRunJob(RuntimeMoniker.NetCoreApp21)]
    [ShortRunJob(RuntimeMoniker.NetCoreApp31)]
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

            using var resource =
                typeof(DecompressAll).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData." + FileName);

            using var compressStream = new SnappyStream(_memoryStream, CompressionMode.Compress, true);

            // ReSharper disable once PossibleNullReferenceException
            resource.CopyTo(compressStream);
            compressStream.Flush();

            _buffer = new byte[65536];
        }


        [Benchmark]
        public void Snappier()
        {
            _memoryStream.Position = 0;
            using var stream = new SnappyStream(_memoryStream, CompressionMode.Decompress, true);

            while (stream.Read(_buffer, 0, _buffer.Length) > 0)
            {
            }
        }

        [Benchmark(Baseline = true)]
        public void PInvoke()
        {
            _memoryStream.Position = 0;
            using var stream = new global::Snappy.SnappyStream(_memoryStream, CompressionMode.Decompress, true);

            while (stream.Read(_buffer, 0, _buffer.Length) > 0)
            {
            }
        }
    }
}
