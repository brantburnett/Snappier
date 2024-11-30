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
            AddJob(baseJob
                .WithRuntime(ClrRuntime.Net48));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core60));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core80)
                .WithPgo(true));
            AddJob(baseJob
                .WithRuntime(CoreRuntime.Core90)
                .WithPgo(true));

            AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByJob);

            HideColumns(Column.EnvironmentVariables);
        }
    }
}
