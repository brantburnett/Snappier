using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks
{
    public class VersionComparisonConfig : StandardConfig
    {
        public VersionComparisonConfig(Job baseJob)
        {
            var jobBefore = baseJob.WithCustomBuildConfiguration("Previous");

            var jobBefore48 = jobBefore.WithRuntime(ClrRuntime.Net48);
            var jobBefore60 = jobBefore.WithRuntime(CoreRuntime.Core60).AsBaseline();
            var jobBefore70 = jobBefore.WithRuntime(CoreRuntime.Core70);
            var jobBefore70Pgo = jobBefore70.WithEnvironmentVariable("DOTNET_TieredPGO", "1");

            var jobAfter48 = baseJob.WithRuntime(ClrRuntime.Net48);
            var jobAfter60 = baseJob.WithRuntime(CoreRuntime.Core60);
            var jobAfter70 = baseJob.WithRuntime(CoreRuntime.Core70);
            var jobAfter70Pgo = jobAfter70.WithEnvironmentVariable("DOTNET_TieredPGO", "1");

            AddJob(jobBefore48);
            AddJob(jobBefore60);
            AddJob(jobBefore70);
            AddJob(jobBefore70Pgo);

            AddJob(jobAfter48);
            AddJob(jobAfter60);
            AddJob(jobAfter70);
            AddJob(jobAfter70Pgo);

            this.KeepBenchmarkFiles();
        }
    }
}
