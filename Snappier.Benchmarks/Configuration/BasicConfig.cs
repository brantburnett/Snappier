using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks.Configuration;

public class BasicConfig : StandardConfig
{
    public BasicConfig(Job baseJob)
    {
        AddJob(baseJob);
    }
}
