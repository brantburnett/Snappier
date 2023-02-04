using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    public class FrameworkCompareConfig : StandardConfig
    {
        public FrameworkCompareConfig()
        {
            AddJob(Job.MediumRun
                .WithRuntime(ClrRuntime.Net48)
                .WithId(".NET 4.8"));
            AddJob(Job.MediumRun
                .WithRuntime(CoreRuntime.Core60)
                .WithId(".NET 6.0"));
            AddJob(Job.MediumRun
                .WithRuntime(CoreRuntime.Core70)
                .WithId(".NET 7.0"));
        }
    }
}
