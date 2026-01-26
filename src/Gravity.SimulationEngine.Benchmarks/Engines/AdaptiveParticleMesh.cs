using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Benchmarks for AdaptiveParticleMesh engine (PM O(N + Grid³ log Grid) FFT-based computation).
/// </summary>
[CPUUsageDiagnoser]
public class AdaptiveParticleMesh : Base
{
	[Benchmark]
	public double Bodies1000Steps1000() 
		=> Run(AdaptiveParticleMesh, ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Bodies10000Steps100() 
		=> Run(AdaptiveParticleMesh, ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Bodies10000Steps1000() 
		=> Run(AdaptiveParticleMesh, ResourcePaths.TenKBodiesSimulation, 1000);
}
