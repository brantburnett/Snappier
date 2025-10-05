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

namespace Snappier.Benchmarks.Configuration;

public class VersionComparisonConfig : StandardConfig
{
    public VersionComparisonConfig(Job baseJob)
    {
        Job jobBefore = baseJob.WithCustomBuildConfiguration("Previous");

        Job jobBefore80 = jobBefore.WithRuntime(CoreRuntime.Core80).WithPgo().AsBaseline();
        Job jobBefore90 = jobBefore.WithRuntime(CoreRuntime.Core90).WithPgo().AsBaseline();
        Job jobBefore10_0 = jobBefore.WithRuntime(CoreRuntime.Core10_0).WithPgo().AsBaseline();

        Job jobAfter80 = baseJob.WithRuntime(CoreRuntime.Core80).WithPgo();
        Job jobAfter90 = baseJob.WithRuntime(CoreRuntime.Core90).WithPgo();
        Job jobAfter10_0 = baseJob.WithRuntime(CoreRuntime.Core10_0).WithPgo();

        AddJob(jobBefore80);
        AddJob(jobBefore90);
        AddJob(jobBefore10_0);
        AddJob(jobAfter80);
        AddJob(jobAfter90);
        AddJob(jobAfter10_0);

#if NET6_0_OR_GREATER // OperatingSystem check is only available in .NET 6.0 or later, but the runner itself won't be .NET 4 anyway
        if (OperatingSystem.IsWindows())
        {
            Job jobBefore48 = jobBefore.WithRuntime(ClrRuntime.Net48).AsBaseline();
            Job jobAfter48 = baseJob.WithRuntime(ClrRuntime.Net48);

            AddJob(jobBefore48);
            AddJob(jobAfter48);
        }
#endif

        WithOrderer(VersionComparisonOrderer.Default);

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
