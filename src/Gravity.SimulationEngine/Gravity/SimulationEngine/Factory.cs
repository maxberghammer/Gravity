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
		Direct,
		HierarchicalBlockDirect,
		AdaptiveDirect,
		AdaptiveBarnesHut,
		AdaptiveParticleMesh,
		AdaptiveFastMultipole
	}

	#endregion

	#region Interface

	public static ISimulationEngine Create(SimulationEngineType type)
		=> type switch
		   {
			   SimulationEngineType.Direct => new(new SemiImplicit(),
												  new None(),
												  new Direct(new Grid()),
												  new Grid()),
			   SimulationEngineType.AdaptiveBarnesHut => new(new Leapfrog(),
															 new MinDiameterCrossingTime(64,
																						 TimeSpan.FromSeconds(1e-7),
																						 0.5),
															 new BarnesHut(),
															 new Grid()),
			   SimulationEngineType.AdaptiveParticleMesh => new(new Leapfrog(),
																new MinDiameterCrossingTime(64,
																							TimeSpan.FromSeconds(1e-7),
																							0.5),
																new ParticleMesh(),
																new Grid()),
			   SimulationEngineType.AdaptiveFastMultipole => new(new Leapfrog(),
																 new MinDiameterCrossingTime(64,
																							 TimeSpan.FromSeconds(1e-7),
																							 0.5),
																 new FastMultipole(),
																 new Grid()),
			   SimulationEngineType.HierarchicalBlockDirect => new(new Leapfrog(),
																   new HierarchicalBlock(4, TimeSpan.FromSeconds(1e-7)),
																   new Direct(new Grid()),
																   new Grid()),
			   SimulationEngineType.AdaptiveDirect => new Implementation.SimulationEngine(new Leapfrog(),
																						  new MinDiameterCrossingTime(64,
																													  TimeSpan.FromSeconds(1e-7),
																													  0.5),
																						  new Direct(new Grid()),
																						  new Grid()),
			   var _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		   };

	#endregion
}