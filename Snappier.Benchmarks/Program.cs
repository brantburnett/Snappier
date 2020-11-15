using System.Reflection;
using BenchmarkDotNet.Running;
using Snappier.Benchmarks;

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args, new StandardConfig());
