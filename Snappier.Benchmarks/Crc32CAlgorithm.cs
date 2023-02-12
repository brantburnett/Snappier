using System;
using BenchmarkDotNet.Attributes;

namespace Snappier.Benchmarks
{
    public class Crc32CAlgorithm
    {
        private byte[] _buffer;

        [GlobalSetup]
        public void Setup()
        {
            _buffer = new byte[65536];
            new Random().NextBytes(_buffer);
        }

        [Benchmark]
        public uint Default()
        {
            return Internal.Crc32CAlgorithm.Append(0, _buffer);
        }
    }
}
