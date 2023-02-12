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

            var job70 = baseJob.WithRuntime(CoreRuntime.Core70);
            AddJob(job70);
            AddJob(job70.WithPgo());

            AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByJob);

            AddColumn(PgoColumn.Default);
            HideColumns(Column.EnvironmentVariables);
        }
    }
}
