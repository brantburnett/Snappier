using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [MediumRunJob(RuntimeMoniker.NetCoreApp21)]
    [MediumRunJob(RuntimeMoniker.NetCoreApp31)]
    public class BlockDecompressAlice
    {
        private ReadOnlyMemory<byte> _input;

        [GlobalSetup]
        public void LoadToMemory()
        {
            using var resource =
                typeof(DecompressAlice).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.alice29.txt");

            var input = new byte[65536]; // Just test the first 64KB
            // ReSharper disable once PossibleNullReferenceException
            resource.Read(input, 0, input.Length);

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);

            _input = compressed.AsMemory(0, compressedLength);
        }


        [Benchmark(Baseline = true)]
        public void Snappier()
        {
            var decompressor = new SnappyDecompressor();

            decompressor.Decompress(_input.Span);
        }
    }
}
