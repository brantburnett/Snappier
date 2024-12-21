using System;
using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class Overview
    {
        private MemoryStream _htmlStream;
        private Memory<byte> _htmlMemory;

        private ReadOnlyMemory<byte> _compressed;
        private MemoryStream _compressedStream;

        private Memory<byte> _outputBuffer;
        private byte[] _streamOutputBuffer;
        private MemoryStream _streamOutput;

        [GlobalSetup]
        public void LoadToMemory()
        {
            _htmlStream = new MemoryStream();
            using var resource =
                typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");
            resource!.CopyTo(_htmlStream);
            _htmlStream.Position = 0;

            byte[] input = new byte[65536]; // Just test the first 64KB
            // ReSharper disable once PossibleNullReferenceException
            int inputLength = _htmlStream.Read(input, 0, input.Length);
            _htmlMemory = input.AsMemory(0, inputLength);

            byte[] compressed = new byte[Snappy.GetMaxCompressedLength(inputLength)];
            int compressedLength = Snappy.Compress(_htmlMemory.Span, compressed);

            _compressed = compressed.AsMemory(0, compressedLength);

            _compressedStream = new MemoryStream();
            using var resource2 =
                typeof(DecompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html_x_4.snappy");
            // ReSharper disable once PossibleNullReferenceException
            resource2.CopyTo(_compressedStream);

            _outputBuffer = new byte[Snappy.GetMaxCompressedLength(inputLength)];
            _streamOutputBuffer = new byte[16384];
            _streamOutput = new MemoryStream();
        }

        [Benchmark]
        public int BlockCompress64KbHtml()
        {
            using var compressor = new SnappyCompressor();

#pragma warning disable CS0618 // Type or member is obsolete
            return compressor.Compress(_htmlMemory.Span, _outputBuffer.Span);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Benchmark]
        public void BlockDecompress64KbHtml()
        {
            using var decompressor = new SnappyDecompressor();

            decompressor.Decompress(_compressed.Span);
        }

        [Benchmark]
        public void StreamCompressHtml()
        {
            _htmlStream.Position = 0;
            _streamOutput.Position = 0;
            using var stream = new SnappyStream(_streamOutput, CompressionMode.Compress, true);

            _htmlStream.CopyTo(stream, 16384);
            stream.Flush();
        }

        [Benchmark]
        public void StreamDecompressHtml()
        {
            _compressedStream.Position = 0;
            using var stream = new SnappyStream(_compressedStream, CompressionMode.Decompress, true);

            while (stream.Read(_streamOutputBuffer, 0, _streamOutputBuffer.Length) > 0)
            {
            }
        }
    }
}
