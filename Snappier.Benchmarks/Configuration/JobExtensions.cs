using BenchmarkDotNet.Jobs;

namespace Snappier.Benchmarks.Configuration;

public static class JobExtensions
{
    public static Job WithPgo(this Job job, bool enabled = true) =>
        job.WithEnvironmentVariable(PgoColumn.PgoEnvironmentVariableName, enabled ? "1" : "0");
}
