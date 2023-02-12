using System;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class BlockDecompressHtml
    {
        private ReadOnlyMemory<byte> _input;
        private Memory<byte> _output;

        [GlobalSetup]
        public void LoadToMemory()
        {
            using var resource =
                typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

            byte[] input = new byte[65536]; // Just test the first 64KB
            // ReSharper disable once PossibleNullReferenceException
            int inputLength = resource!.Read(input, 0, input.Length);

            byte[] compressed = new byte[Snappy.GetMaxCompressedLength(inputLength)];
            int compressedLength = Snappy.Compress(input.AsSpan(0, inputLength), compressed);

            _input = compressed.AsMemory(0, compressedLength);

            _output = new byte[65536];
        }


        [Benchmark]
        public void Decompress()
        {
            var decompressor = new SnappyDecompressor();

            decompressor.Decompress(_input.Span);
            decompressor.Read(_output.Span);
        }
    }
}
