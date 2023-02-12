using System;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class BlockDecompressHtml
    {
        private ReadOnlyMemory<byte> _input;

        [GlobalSetup]
        public void LoadToMemory()
        {
            using var resource =
                typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

            var input = new byte[65536]; // Just test the first 64KB
            // ReSharper disable once PossibleNullReferenceException
            resource.Read(input, 0, input.Length);

            var compressed = new byte[Snappy.GetMaxCompressedLength(input.Length)];
            var compressedLength = Snappy.Compress(input, compressed);

            _input = compressed.AsMemory(0, compressedLength);
        }


        [Benchmark]
        public void Decompress()
        {
            var decompressor = new SnappyDecompressor();

            decompressor.Decompress(_input.Span);
        }
    }
}
