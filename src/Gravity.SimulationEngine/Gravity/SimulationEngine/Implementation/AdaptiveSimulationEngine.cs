using System;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class AdaptiveSimulationEngine : ISimulationEngine
{
	#region Implementation of ISimulationEngine

	void ISimulationEngine.Simulate(Entity[] entities, TimeSpan deltaTime)
		=> throw new NotImplementedException();

	#endregion
}