using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Snappier.Benchmarks.Configuration
{
    public class VersionComparisonConfig : StandardConfig
    {
        public VersionComparisonConfig(Job baseJob)
        {
            var jobBefore = baseJob.WithCustomBuildConfiguration("Previous");

            var jobBefore48 = jobBefore.WithRuntime(ClrRuntime.Net48).AsBaseline();
            var jobBefore60 = jobBefore.WithRuntime(CoreRuntime.Core60).AsBaseline();
            var jobBefore70 = jobBefore.WithRuntime(CoreRuntime.Core70).AsBaseline();
            var jobBefore70Pgo = jobBefore70.WithPgo();

            var jobAfter48 = baseJob.WithRuntime(ClrRuntime.Net48);
            var jobAfter60 = baseJob.WithRuntime(CoreRuntime.Core60);
            var jobAfter70 = baseJob.WithRuntime(CoreRuntime.Core70);
            var jobAfter70Pgo = jobAfter70.WithPgo();

            AddJob(jobBefore48);
            AddJob(jobBefore60);
            AddJob(jobBefore70);
            AddJob(jobBefore70Pgo);

            AddJob(jobAfter48);
            AddJob(jobAfter60);
            AddJob(jobAfter70);
            AddJob(jobAfter70Pgo);

            WithOrderer(VersionComparisonOrderer.Default);

            AddColumn(PgoColumn.Default);
            HideColumns(Column.EnvironmentVariables, Column.Job);
        }

        private class VersionComparisonOrderer : IOrderer
        {
            public static readonly IOrderer Default = new VersionComparisonOrderer();

            public IEnumerable<BenchmarkCase> GetExecutionOrder(ImmutableArray<BenchmarkCase> benchmarksCase,
                IEnumerable<BenchmarkLogicalGroupRule> order = null) =>
                benchmarksCase
                    .OrderBy(p => p.Job.Environment.Runtime.MsBuildMoniker)
                    .ThenBy(p => PgoColumn.IsPgo(p) ? 1 : 0)
                    .ThenBy(p => p.DisplayInfo)
                    .ThenBy(p => !p.Descriptor.Baseline);

            public IEnumerable<BenchmarkCase> GetSummaryOrder(ImmutableArray<BenchmarkCase> benchmarksCases,
                Summary summary) =>
                GetExecutionOrder(benchmarksCases);

            public string GetHighlightGroupKey(BenchmarkCase benchmarkCase) => null;

            public string GetLogicalGroupKey(ImmutableArray<BenchmarkCase> allBenchmarksCases,
                BenchmarkCase benchmarkCase) =>
                $"{benchmarkCase.Job.Environment.Runtime.MsBuildMoniker}-Pgo={(PgoColumn.IsPgo(benchmarkCase) ? "Y" : "N")}";

            public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
                IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
                IEnumerable<BenchmarkLogicalGroupRule> order = null) =>
                logicalGroups.OrderBy(p => p.Key);

            public bool SeparateLogicalGroups => true;
        }
    }
}
