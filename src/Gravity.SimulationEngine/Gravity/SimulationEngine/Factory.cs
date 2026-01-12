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
			SimulationEngineType.Standard                => new Implementation.Standard.SimulationEngine(),
			SimulationEngineType.BarnesHutWithRungeKutta => new Implementation.BarnesHut.SimulationEngine(new RungeKuttaIntegrator()),
			SimulationEngineType.BarnesHutWithLeapfrog   => new Implementation.BarnesHut.SimulationEngine(new LeapfrogIntegrator()),
			SimulationEngineType.ClusteredNBody          => new Implementation.ClusteredNBody.SimulationEngine(),
			SimulationEngineType.Adaptive                => new Implementation.Adaptive.SimulationEngine(),
			_                                            => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		};

	#endregion
}