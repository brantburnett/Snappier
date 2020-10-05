using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    //[ShortRunJob(RuntimeMoniker.NetCoreApp21)]
    [ShortRunJob(RuntimeMoniker.NetCoreApp31)]
    [MemoryDiagnoser]
    public class CompressAlice
    {
        private MemoryStream _source;
        private MemoryStream _destination;

        [Params(1024, 16384, 131072)]
        public int ReadSize;

        [GlobalSetup]
        public void LoadToMemory()
        {
            _source = new MemoryStream();
            _destination = new MemoryStream();

            using var resource =
                typeof(DecompressAlice).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.alice29.txt");

            // ReSharper disable once PossibleNullReferenceException
            resource.CopyTo(_source);
        }


        [Benchmark]
        public void Snappier()
        {
            _source.Position = 0;
            _destination.Position = 0;
            using var stream = new SnappyStream(_destination, CompressionMode.Compress, true);

            _source.CopyTo(_destination, ReadSize);
        }

        [Benchmark(Baseline = true)]
        public void PInvoke()
        {
            _source.Position = 0;
            _destination.Position = 0;
            using var stream = new Snappy.SnappyStream(_destination, CompressionMode.Compress, true);

            _source.CopyTo(_destination, ReadSize);
        }
    }
}
