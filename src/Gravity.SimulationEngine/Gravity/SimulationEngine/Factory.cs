using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine;

public static class Factory
{
	#region Interface

	public static ISimulationEngine CreateStandard()
		=> new StandardSimulationEngine();

	public static ISimulationEngine CreateBarnesHut()
		=> new BarnesHutSimulationEngine();

	public static ISimulationEngine CreateBarnesHutWithLeapfrog()
		=> new BarnesHutSimulationEngine(new LeapfrogIntegrator());

	#endregion
}