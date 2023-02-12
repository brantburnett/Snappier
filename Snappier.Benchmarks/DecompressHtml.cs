using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace Snappier.Benchmarks
{
    public class DecompressHtml
    {
        private MemoryStream _memoryStream;
        private byte[] _buffer;

        [Params(16384)]
        public int ReadSize;

        [GlobalSetup]
        public void LoadToMemory()
        {
            _memoryStream = new MemoryStream();

            using var resource =
                typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html_x_4.snappy");

            // ReSharper disable once PossibleNullReferenceException
            resource.CopyTo(_memoryStream);

            _buffer = new byte[ReadSize];
        }

        [Benchmark]
        public void Decompress()
        {
            _memoryStream.Position = 0;
            using var stream = new SnappyStream(_memoryStream, CompressionMode.Decompress, true);

            while (stream.Read(_buffer, 0, ReadSize) > 0)
            {
            }
        }
    }
}
