using System;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine;

public static class Factory
{
	public enum SimulationEngineType
	{
		Standard,
		BarnesHutWithRungeKutta,
		BarnesHutWithLeapfrog,
		ClusteredNBody,
		Adaptive
	}

	#region Interface

	public static ISimulationEngine Create(SimulationEngineType type)
		=> type switch
		{
			SimulationEngineType.Standard                => new StandardSimulationEngine(),
			SimulationEngineType.BarnesHutWithRungeKutta => new BarnesHutSimulationEngine(new RungeKuttaIntegrator()),
			SimulationEngineType.BarnesHutWithLeapfrog   => new BarnesHutSimulationEngine(new LeapfrogIntegrator()),
			SimulationEngineType.ClusteredNBody          => new ClusteredNBodySimulationEngine(),
			SimulationEngineType.Adaptive                => new AdaptiveSimulationEngine(),
			_                                            => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		};

	#endregion
}