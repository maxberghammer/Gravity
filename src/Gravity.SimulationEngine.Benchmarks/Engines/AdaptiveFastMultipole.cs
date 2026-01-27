using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for AdaptiveFastMultipole engine (FMM O(N) multipole expansion).
/// </summary>
[CPUUsageDiagnoser]
public class AdaptiveFastMultipole : Base
{
	#region Interface

	[Benchmark]
	public double Bodies1000Steps1000()
		=> Run(AdaptiveFastMultipole, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100()
		=> Run(AdaptiveFastMultipole, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000()
		=> Run(AdaptiveFastMultipole, ResourcePaths.TenKBodiesSimulation, 1000);

	#endregion
}