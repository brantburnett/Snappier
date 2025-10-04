namespace Snappier.Benchmarks.Configuration;

public static class JobExtensions
{
    extension(Job job)
    {
        public Job WithPgo(bool enabled = true) =>
            job.WithEnvironmentVariable(PgoColumn.PgoEnvironmentVariableName, enabled ? "1" : "0");
    }
}
