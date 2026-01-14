// Erstellt am: 12.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;

namespace Gravity.SimulationEngine.Benchmarks.Common;

public abstract class AllEnginesBenchBase
{
	#region Fields

	private TimeSpan _dt;
	private IWorld _world = null!;

	#endregion

	#region Interface

	[GlobalSetup]
	public async Task SetupAsync()
	{
		// Always target 1000-bodies resource; steps default to BenchParams.Steps (1000)
		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(JsonResourcePath);
		_world = world;
		_dt = dt;

		Standard = Factory.Create(Factory.SimulationEngineType.Standard);
		BarnesHutWithLeapfrog = Factory.Create(Factory.SimulationEngineType.BarnesHutWithLeapfrog);
		BarnesHutWithRungeKutta = Factory.Create(Factory.SimulationEngineType.BarnesHutWithRungeKutta);
		ClusteredNBody = Factory.Create(Factory.SimulationEngineType.ClusteredNBody);
		Adaptive = Factory.Create(Factory.SimulationEngineType.Adaptive);
	}

	#endregion

	#region Implementation

	protected abstract string JsonResourcePath { get; }

	protected abstract int Steps { get; }

	protected ISimulationEngine Adaptive { get; private set; } = null!;

	protected ISimulationEngine BarnesHutWithLeapfrog { get; private set; } = null!;

	protected ISimulationEngine BarnesHutWithRungeKutta { get; private set; } = null!;

	protected ISimulationEngine ClusteredNBody { get; private set; } = null!;

	protected ISimulationEngine Standard { get; private set; } = null!;

	protected double Run(ISimulationEngine engine)
	{
		ArgumentNullException.ThrowIfNull(engine);
		var world = _world.CreateMock();
		var bodies = world.GetBodies();
		var sum = 0d;

		for(var i = 0; i < Steps; i++)
		{
			engine.Simulate(world, _dt);

			sum += bodies.Sum(body => body.v.Length);
		}

		return sum;
	}

	#endregion
}