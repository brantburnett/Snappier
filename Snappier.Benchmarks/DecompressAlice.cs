using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    //[ShortRunJob(RuntimeMoniker.NetCoreApp21)]
    [ShortRunJob(RuntimeMoniker.NetCoreApp31)]
    [MemoryDiagnoser]
    public class DecompressAlice
    {
        private MemoryStream _memoryStream;
        private byte[] _buffer;

        [Params(1024, 16384, 131072)]
        public int ReadSize;

        [GlobalSetup]
        public void LoadToMemory()
        {
            _memoryStream = new MemoryStream();

            using var resource =
                typeof(DecompressAlice).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.alice29.snappy");

            // ReSharper disable once PossibleNullReferenceException
            resource.CopyTo(_memoryStream);

            _buffer = new byte[ReadSize];
        }


        [Benchmark]
        public void Snappier()
        {
            _memoryStream.Position = 0;
            using var stream = new SnappyStream(_memoryStream, CompressionMode.Decompress, true);

            while (stream.Read(_buffer, 0, ReadSize) > 0)
            {
            }
        }

        [Benchmark(Baseline = true)]
        public void PInvoke()
        {
            _memoryStream.Position = 0;
            using var stream = new Snappy.SnappyStream(_memoryStream, CompressionMode.Decompress, true);

            while (stream.Read(_buffer, 0, ReadSize) > 0)
            {
            }
        }
    }
}
