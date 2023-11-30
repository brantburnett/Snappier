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

            var job80 = baseJob.WithRuntime(CoreRuntime.Core80);
            AddJob(job80.WithPgo(false));
            AddJob(job80.WithPgo(true));

            AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByJob);

            AddColumn(PgoColumn.Default);
            HideColumns(Column.EnvironmentVariables);
        }
    }
}
