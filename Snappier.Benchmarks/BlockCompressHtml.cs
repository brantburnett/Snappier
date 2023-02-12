﻿using System;
using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class BlockCompressHtml
    {
        private ReadOnlyMemory<byte> _input;
        private Memory<byte> _output;

        [GlobalSetup]
        public void LoadToMemory()
        {
            using var resource =
                typeof(BlockCompressHtml).Assembly.GetManifestResourceStream("Snappier.Benchmarks.TestData.html");

            byte[] input = new byte[resource!.Length];
            int inputLength = resource!.Read(input, 0, input.Length);
            _input = input.AsMemory(0, inputLength);

            _output = new byte[Snappy.GetMaxCompressedLength(inputLength)];
        }

        [Benchmark]
        public int Compress()
        {
            using var compressor = new SnappyCompressor();

            return compressor.Compress(_input.Span, _output.Span);
        }
    }
}
