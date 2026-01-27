using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for HierarchicalBlockDirect engine.
/// Uses hierarchical timesteps with O(n²) direct computation.
/// Optimized for systems with mixed timescales (e.g., planetary systems with moons).
/// </summary>
[CPUUsageDiagnoser]
public class HierarchicalBlockDirect : Base
{
	#region Interface

	[Benchmark]
	public double Bodies1000Steps1000()
		=> Run(HierarchicalBlockDirect, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100()
		=> Run(HierarchicalBlockDirect, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000()
		=> Run(HierarchicalBlockDirect, ResourcePaths.TenKBodiesSimulation, 1000);

	#endregion
}
