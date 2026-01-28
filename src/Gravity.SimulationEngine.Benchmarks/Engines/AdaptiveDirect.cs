using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for AdaptiveDirect engine.
/// Uses adaptive timesteps with O(n²) direct computation.
/// Comparison baseline for HierarchicalBlockDirect.
/// </summary>
[CPUUsageDiagnoser]
public class AdaptiveDirect : Base
{
	#region Interface

	[Benchmark]
	public double Bodies1000Steps1000()
		=> Run(AdaptiveDirect, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100()
		=> Run(AdaptiveDirect, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000()
		=> Run(AdaptiveDirect, ResourcePaths.TenKBodiesSimulation, 1000);

	#endregion
}
