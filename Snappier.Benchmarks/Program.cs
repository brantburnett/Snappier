using System;
using BenchmarkDotNet.Running;

namespace Snappier.Benchmarks
{
    class Program
    {
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new StandardConfig());
    }
}
