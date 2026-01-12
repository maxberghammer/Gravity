using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Benchmarks.Common;
using Gravity.SimulationEngine.Mock;

namespace Gravity.SimulationEngine.Benchmarks;

public class AllEngines10000Bodies100StepsBench : AllEnginesBenchBase
{
	#region Interface

	[Benchmark]
	public double Standard10000Bodies100Steps()
		=> Run(Standard);

	[Benchmark]
	public double BarnesHutLeapfrog10000Bodies100Steps()
		=> Run(BarnesHutWithLeapfrog);

	[Benchmark]
	public double BarnesHutRungeKutta10000Bodies100Steps()
		=> Run(BarnesHutWithRungeKutta);

	[Benchmark]
	public double Clustered10000Bodies100Steps()
		=> Run(ClusteredNBody);

	[Benchmark]
	public double Adaptive10000Bodies100Steps()
		=> Run(Adaptive);

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override string JsonResourcePath
		=> ResourcePaths.TenKBodiesSimulation;

	/// <inheritdoc/>
	protected override int Steps
		=> 100;

	#endregion
}