using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    public class BasicConfig : StandardConfig
    {
        public BasicConfig(Job baseJob)
        {
            AddJob(baseJob);
        }
    }
}
