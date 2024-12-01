#if !PREVIOUS

using BenchmarkDotNet.Attributes;
using Snappier.Internal;

namespace Snappier.Benchmarks
{
    public class VarIntEncodingRead
    {
        [Params(0u, 256u, 65536u)]
        public uint Value { get; set; }

        readonly byte[] _source = new byte[16];

        [GlobalSetup]
        public void GlobalSetup()
        {
            VarIntEncoding.Write(_source, Value);
        }

        [Benchmark(Baseline = true)]
        public (int, uint) Previous()
        {
            var length = VarIntEncoding.ReadSlow(_source, out var result);

            return (length, result);
        }

        [Benchmark]
        public (int, uint) New()
        {
            var length = VarIntEncoding.Read(_source, out var result);

            return (length, result);
        }
    }
}

#endif
