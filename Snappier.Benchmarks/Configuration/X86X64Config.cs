using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.NetCoreApp;

namespace Snappier.Benchmarks.Configuration;

public class X86X64Config : StandardConfig
{
    public X86X64Config(Job baseJob)
    {
        NetCoreAppSettings dotnetCli32Bit = new()
        {
            CliPath = new FileInfo(@"C:\Program Files (x86)\dotnet\dotnet.exe"),
        };

        NetCoreAppSettings dotnetCli64Bit = new()
        {
            CliPath = new FileInfo(@"C:\Program Files\dotnet\dotnet.exe"),
        };

        AddJob(baseJob
            .WithToolchain(CsProjCoreToolchain.From(CoreRuntime.Core10_0, dotnetCli32Bit))
            .WithPlatform(Platform.X86)
            .WithId("x86"));
        AddJob(baseJob
            .WithToolchain(CsProjCoreToolchain.From(CoreRuntime.Core10_0, dotnetCli64Bit))
            .WithPlatform(Platform.X64)
            .WithId("x64"));

        AddDiagnoser(new DisassemblyDiagnoser(
            new DisassemblyDiagnoserConfig(maxDepth: 2, printSource: true, exportDiff: true)));
    }
}
