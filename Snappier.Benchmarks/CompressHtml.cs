using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    [Config(typeof(FrameworkCompareConfig))]
    public class CompressHtml
    {
        private ReadOnlyMemory<byte> _buffer;
        private Memory<byte> _buffer2;

        private SnappyCompressor _snappyCompressor;
        //private SnappyCompressor2 _snappyCompressor2;

        public void GlobalSetup()
        {
            using var memoryStream = new MemoryStream();

            using var resource =
                typeof(DecompressAlice).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

            // ReSharper disable once PossibleNullReferenceException
            resource.CopyTo(memoryStream);

            _buffer = memoryStream.ToArray().AsMemory();
            _buffer2 = new byte[Helpers.MaxCompressedLength(_buffer.Length)].AsMemory();
        }

        [GlobalSetup(Target = nameof(Snappier))]
        public void SnappierSetup()
        {
            GlobalSetup();

            _snappyCompressor = new SnappyCompressor();
        }

        //[GlobalSetup(Target = nameof(Snappier2))]
        //public void Snappier2Setup()
        //{
        //    GlobalSetup();

        //    _snappyCompressor2 = new SnappyCompressor2();
        //}

        [GlobalCleanup]
        public void Cleanup()
        {
            _snappyCompressor?.Dispose();
            //_snappyCompressor2?.Dispose();
        }

        [Benchmark(Baseline = true)]
        public void Snappier()
        {
            _snappyCompressor.Compress(_buffer.Span, _buffer2.Span);
        }

        //[Benchmark]
        //public void Snappier2()
        //{
        //    _snappyCompressor2.Compress(_buffer.Span, _buffer2.Span);
        //}
    }
}
