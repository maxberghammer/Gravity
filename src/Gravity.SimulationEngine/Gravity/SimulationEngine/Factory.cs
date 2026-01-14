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
		Adaptive
	}

	#endregion

	#region Interface

	public static ISimulationEngine Create(SimulationEngineType type)
		=> type switch
		   {
			   SimulationEngineType.Standard => new Implementation.Standard.SimulationEngine(),
			   SimulationEngineType.Adaptive => new Implementation.Adaptive.SimulationEngine(new LeapfrogIntegrator(),
																							 new MinDiameterCrossingTimeOversampler( // Obergrenze pro Frame 
																																	64,
																																	// Untergrenze für numerische Stabilität
																																	TimeSpan.FromSeconds(1e-6),
																																	// Leapfrog erlaubt etwas größere Schritte
																																	0.8)),
			   var _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		   };

	#endregion
}