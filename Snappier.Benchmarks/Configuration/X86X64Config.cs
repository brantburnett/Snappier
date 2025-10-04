using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;

namespace Snappier.Benchmarks.Configuration;

public class X86X64Config : StandardConfig
{
    public X86X64Config(Job baseJob)
    {
        NetCoreAppSettings dotnetCli32Bit = NetCoreAppSettings
            .NetCoreApp50
            .WithCustomDotNetCliPath(@"C:\Program Files (x86)\dotnet\dotnet.exe", "32 bit cli");

        NetCoreAppSettings dotnetCli64Bit = NetCoreAppSettings
            .NetCoreApp50
            .WithCustomDotNetCliPath(@"C:\Program Files\dotnet\dotnet.exe", "64 bit cli");

        AddJob(baseJob
            .WithToolchain(CsProjCoreToolchain.From(dotnetCli32Bit))
            .WithPlatform(Platform.X86)
            .WithId("x86"));
        AddJob(baseJob
            .WithToolchain(CsProjCoreToolchain.From(dotnetCli64Bit))
            .WithPlatform(Platform.X64)
            .WithId("x64"));

        AddDiagnoser(new DisassemblyDiagnoser(
            new DisassemblyDiagnoserConfig(maxDepth: 2, printSource: true, exportDiff: true)));
    }
}
