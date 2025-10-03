using System;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks.Configuration
{
    public class FrameworkCompareConfig : StandardConfig
    {
        public FrameworkCompareConfig(Job baseJob)
        {
            #if NET6_0_OR_GREATER  // OperatingSystem check is only available in .NET 6.0 or later, but the runner itself won't be .NET 4 anyway
            if (OperatingSystem.IsWindows())
            {
                AddJob(baseJob
                    .WithRuntime(ClrRuntime.Net48));
            }
            #endif

            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core80)
                .WithPgo(true));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core90)
                .WithPgo(true));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core10_0)
                .WithPgo(true));

            AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByJob);

            HideColumns(Column.EnvironmentVariables);
        }
    }
}
