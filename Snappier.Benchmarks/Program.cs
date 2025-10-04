using BenchmarkDotNet.Running;
using Snappier.Benchmarks.Configuration;

Console.WriteLine("Select configuration:");
Console.WriteLine("  #0 Frameworks Short");
Console.WriteLine("  #1 Frameworks Default");
Console.WriteLine("  #2 Version Comparison Short");
Console.WriteLine("  #3 Version Comparison Default");
Console.WriteLine("  #4 Basic Short");
Console.WriteLine("  #5 Basic Default");
Console.WriteLine("  #6 x86/x64 Short");
Console.WriteLine("  #7 x86/x64 Default");

Console.WriteLine();
Console.Write("Selection: ");

string input = Console.ReadLine();
Console.WriteLine();

StandardConfig config = input switch
{
    "0" => (StandardConfig) new FrameworkCompareConfig(Job.ShortRun),
    "1" => new FrameworkCompareConfig(Job.Default),
    "2" => new VersionComparisonConfig(Job.ShortRun),
    "3" => new VersionComparisonConfig(Job.Default),
    "4" => new BasicConfig(Job.ShortRun),
    "5" => new BasicConfig(Job.Default),
    "6" => new X86X64Config(Job.ShortRun),
    "7" => new X86X64Config(Job.Default),
    _ => null
};

if (config is not null)
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
}
