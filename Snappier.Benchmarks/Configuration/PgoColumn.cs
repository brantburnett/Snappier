using System.Linq;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Snappier.Benchmarks.Configuration;

public class PgoColumn : IColumn
{
    public static readonly IColumn Default = new PgoColumn();

    public const string PgoEnvironmentVariableName = "DOTNET_TieredPGO";

    public string Id => "PGO";
    public string ColumnName => "PGO";

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => !IsPgo(benchmarkCase);
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => IsPgo(benchmarkCase) ? "Y" : "N";

    public static bool IsPgo(BenchmarkCase benchmarkCase) =>
        benchmarkCase.Job.Environment.EnvironmentVariables?.Any(p => p.Key == PgoEnvironmentVariableName && p.Value == "1") ?? false;

    public bool IsAvailable(Summary summary) => true;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Job;
    public int PriorityInCategory => 0;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => $"Indicates state of Dynamic PGO";
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public override string ToString() => ColumnName;
}
