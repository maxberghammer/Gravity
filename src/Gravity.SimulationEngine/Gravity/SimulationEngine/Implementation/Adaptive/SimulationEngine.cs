using System;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;
using Gravity.SimulationEngine.Implementation.Oversamplers;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Fields

	private readonly IAccelerationStrategy _strategy;

	#endregion

	#region Construction

	public SimulationEngine(IIntegrator integrator, IOversampler oversampler, IAccelerationStrategy strategy)
		: base(integrator, oversampler)
	{
		_strategy = strategy;
	}

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnComputeAccelerations(IWorld world, Body[] bodies)
		=> _strategy.ComputeAccelerations(bodies, Diagnostics);

	/// <inheritdoc/>
	protected override void OnAfterSimulationStep(IWorld world, Body[] bodies)
		=> ResolveCollisions(world, bodies);

	#endregion
}