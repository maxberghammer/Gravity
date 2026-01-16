using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Benchmarks.Common;
using Gravity.SimulationEngine.Mock;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks;

[CPUUsageDiagnoser]
public class ParticleMeshBench : EngineBenchBase
{
	#region Implementation

	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveParticleMesh;

	[Benchmark]
	public double Run2Bodies10Steps()
		=> Run(ResourcePaths.TwoBodiesSimulation, 10);

	[Benchmark]
	public double Run1000Bodies10Steps()
		=> Run(ResourcePaths.ThousandBodiesSimulation, 10);

	[Benchmark]
	public double Run10000Bodies10Steps()
		=> Run(ResourcePaths.TenKBodiesSimulation, 10);

	[Benchmark]
	public double Run2Bodies100Steps()
		=> Run(ResourcePaths.TwoBodiesSimulation, 100);

	[Benchmark]
	public double Run1000Bodies100Steps()
		=> Run(ResourcePaths.ThousandBodiesSimulation, 100);

	[Benchmark]
	public double Run10000Bodies100Steps()
		=> Run(ResourcePaths.TenKBodiesSimulation, 100);

	[Benchmark]
	public double Run2Bodies1000Steps()
		=> Run(ResourcePaths.TwoBodiesSimulation, 1000);

	[Benchmark]
	public double Run1000Bodies1000Steps()
		=> Run(ResourcePaths.ThousandBodiesSimulation, 1000);

	[Benchmark]
	public double Run10000Bodies1000Steps()
		=> Run(ResourcePaths.TenKBodiesSimulation, 1000);

	#endregion
}
