using System;
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
			   SimulationEngineType.Standard => new Implementation.Standard.SimulationEngine(new SemiImplicitIntegrator(),
																							 new NoOversampler()),
			   SimulationEngineType.AdaptiveBarnesHut => new Implementation.Adaptive.SimulationEngine(new LeapfrogIntegrator(),
																										new MinDiameterCrossingTimeOversampler(64,
																																			   TimeSpan.FromSeconds(1e-6),
																																			   0.8),
																										new Implementation.Adaptive.BarnesHutStrategy()),
			   SimulationEngineType.AdaptiveParticleMesh => new Implementation.Adaptive.SimulationEngine(new LeapfrogIntegrator(),
																										   new MinDiameterCrossingTimeOversampler(64,
																																				  TimeSpan.FromSeconds(1e-6),
																																				  0.8),
																										   new Implementation.Adaptive.ParticleMeshStrategy()),
			   var _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		   };

	#endregion
}