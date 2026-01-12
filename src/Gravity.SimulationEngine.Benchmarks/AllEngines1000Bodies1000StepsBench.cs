using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Benchmarks.Common;
using Gravity.SimulationEngine.Mock;

namespace Gravity.SimulationEngine.Benchmarks;

public class AllEngines1000Bodies1000StepsBench : AllEnginesBenchBase
{
	#region Interface

	[Benchmark]
	public double Standard1000Bodies1000Steps()
		=> Run(Standard);

	[Benchmark]
	public double BarnesHutLeapfrog1000Bodies1000Steps()
		=> Run(BarnesHutWithLeapfrog);

	[Benchmark]
	public double BarnesHutRungeKutta1000Bodies1000Steps()
		=> Run(BarnesHutWithRungeKutta);

	[Benchmark]
	public double Clustered1000Bodies1000Steps()
		=> Run(ClusteredNBody);

	[Benchmark]
	public double Adaptive1000Bodies1000Steps()
		=> Run(Adaptive);

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override string JsonResourcePath
		=> ResourcePaths.ThousandBodiesSimulation;

	/// <inheritdoc/>
	protected override int Steps
		=> 1000;

	#endregion
}