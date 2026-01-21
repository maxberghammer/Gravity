using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for Standard engine (O(n²) direct computation).
/// </summary>
[CPUUsageDiagnoser]
public class Standard : Base
{
	[Benchmark]
	public double Bodies1000Steps1000() 
		=> Run(Standard, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100() 
		=> Run(Standard, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000() 
		=> Run(Standard, ResourcePaths.TenKBodiesSimulation, 1000);
}
