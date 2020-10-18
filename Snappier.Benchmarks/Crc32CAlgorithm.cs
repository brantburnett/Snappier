using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    [SimpleJob(RuntimeMoniker.NetCoreApp21, baseline: true)]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
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
