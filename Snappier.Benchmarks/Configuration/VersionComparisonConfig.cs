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
            var jobBefore80 = jobBefore.WithRuntime(CoreRuntime.Core80).AsBaseline();
            var jobBefore80Pgo = jobBefore80.WithPgo();

            var jobAfter48 = baseJob.WithRuntime(ClrRuntime.Net48);
            var jobAfter60 = baseJob.WithRuntime(CoreRuntime.Core60);
            var jobAfter80 = baseJob.WithRuntime(CoreRuntime.Core80);
            var jobAfter80Pgo = jobAfter80.WithPgo();

            AddJob(jobBefore48);
            AddJob(jobBefore60);
            AddJob(jobBefore80);
            AddJob(jobBefore80Pgo);

            AddJob(jobAfter48);
            AddJob(jobAfter60);
            AddJob(jobAfter80);
            AddJob(jobAfter80Pgo);

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
                    .ThenBy(p => !p.Descriptor.Baseline)
                    .ThenBy(p => p.DisplayInfo);

            public IEnumerable<BenchmarkCase> GetSummaryOrder(ImmutableArray<BenchmarkCase> benchmarksCases,
                Summary summary) =>
                GetExecutionOrder(benchmarksCases);

            public string GetHighlightGroupKey(BenchmarkCase benchmarkCase) => null;

            public string GetLogicalGroupKey(ImmutableArray<BenchmarkCase> allBenchmarksCases,
                BenchmarkCase benchmarkCase) =>
                $"{benchmarkCase.Job.Environment.Runtime.MsBuildMoniker}-Pgo={(PgoColumn.IsPgo(benchmarkCase) ? "Y" : "N")}-{benchmarkCase.Descriptor.MethodIndex}";

            public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
                IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
                IEnumerable<BenchmarkLogicalGroupRule> order = null) =>
                logicalGroups.OrderBy(p => p.Key);

            public bool SeparateLogicalGroups => true;
        }
    }
}
