using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    public class FrameworkCompareConfig : StandardConfig
    {
        public FrameworkCompareConfig()
        {
            AddJob(Job.MediumRun
                .WithRuntime(CoreRuntime.Core21)
                .WithId(".NET Core 2.1"));
            AddJob(Job.MediumRun
                .WithRuntime(CoreRuntime.Core31)
                .WithId(".NET Core 3.1"));
            AddJob(Job.MediumRun
                .WithRuntime(CoreRuntime.Core50)
                .WithId(".NET 5.0"));
        }
    }
}
