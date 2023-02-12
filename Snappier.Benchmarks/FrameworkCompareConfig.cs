using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    public class FrameworkCompareConfig : StandardConfig
    {
        public FrameworkCompareConfig(Job baseJob)
        {
            AddJob(baseJob
                .WithRuntime(ClrRuntime.Net48)
                .WithId(".NET 4.8"));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core60)
                .WithId(".NET 6.0"));

            var job70 = baseJob.WithRuntime(CoreRuntime.Core70);
            AddJob(job70.WithId(".NET 7.0"));
            AddJob(job70.WithId(".NET 7.0 PGO")
                .WithEnvironmentVariable("DOTNET_TieredPGO", "1"));
        }
    }
}
