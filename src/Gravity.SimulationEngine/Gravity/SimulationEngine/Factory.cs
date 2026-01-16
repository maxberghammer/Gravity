using System;
using Gravity.SimulationEngine.Implementation.CollisionResolvers;
using Gravity.SimulationEngine.Implementation.Computations;
using Gravity.SimulationEngine.Implementation.Integrators;
using Gravity.SimulationEngine.Implementation.Oversamplers;

namespace Gravity.SimulationEngine;

public static class Factory
{
	#region Internal types

	public enum SimulationEngineType
	{
		Standard,
		AdaptiveBarnesHut,
		AdaptiveParticleMesh
	}

	#endregion

	#region Interface

	public static ISimulationEngine Create(SimulationEngineType type)
		=> type switch
		   {
			   SimulationEngineType.Standard => new(new SemiImplicit(),
													new None(),
													new Direct(new Simple()),
													new Simple()),
			   SimulationEngineType.AdaptiveBarnesHut => new(new Leapfrog(),
															 new MinDiameterCrossingTime(64,
																						 TimeSpan.FromSeconds(1e-6),
																						 0.8),
															 new BarnesHut(),
															 new Simple()),
			   SimulationEngineType.AdaptiveParticleMesh => new Implementation.SimulationEngine(new Leapfrog(),
																								new MinDiameterCrossingTime(64,
																															TimeSpan.FromSeconds(1e-6),
																															0.8),
																								new ParticleMesh(),
																								new Simple()),
			   var _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		   };

	#endregion
}