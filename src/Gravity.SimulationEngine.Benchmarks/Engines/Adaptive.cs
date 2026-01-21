using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for Adaptive engine (Barnes-Hut O(n log n) computation).
/// </summary>
[CPUUsageDiagnoser]
public class Adaptive : Base
{
	[Benchmark]
	public double Bodies1000Steps1000() 
		=> Run(Adaptive, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100() 
		=> Run(Adaptive, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000() 
		=> Run(Adaptive, ResourcePaths.TenKBodiesSimulation, 1000);
}
