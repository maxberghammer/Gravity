using Gravity.SimulationEngine.Implementation;

namespace Gravity.SimulationEngine;

public static class Factory
{
	#region Interface

	public static ISimulationEngine CreateStandard()
		=> new StandardSimulationEngine();

	public static ISimulationEngine CreateBarnesHut()
		=> new BarnesHutSimulationEngine();

	#endregion
}