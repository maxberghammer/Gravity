using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;

namespace Gravity.SimulationEngine.Benchmarks.Common;

public abstract class EngineBenchBase
{
	#region Fields

	private readonly Dictionary<string, (IWorld World, TimeSpan Dt)> _cache = new();
	private ISimulationEngine _engine = null!;

	#endregion

	#region Interface

	[GlobalSetup]
	public async Task SetupAsync()
	{
		// Preload commonly used resources so JSON I/O is outside measured code
		await PreloadAsync(ResourcePaths.TwoBodiesSimulation);
		await PreloadAsync(ResourcePaths.ThousandBodiesSimulation);
		await PreloadAsync(ResourcePaths.TenKBodiesSimulation);

		_engine = Factory.Create(EngineType);
	}

	#endregion

	#region Implementation

	protected abstract Factory.SimulationEngineType EngineType { get; }

	[SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Benchmark setup preloads async; run path stays sync for BDN")]
	protected double Run(string resourcePath, int steps)
	{
		// Ensure requested resource is loaded (should be in cache already)
		if(!_cache.TryGetValue(resourcePath, out var entry))
		{
			// Fallback: preload synchronously if needed (rare path in CI)
			PreloadAsync(resourcePath).GetAwaiter().GetResult();
			entry = _cache[resourcePath];
		}

		(var world, var dt) = entry;

		world = world.CreateMock();
		double sum = 0;
		var entities = world.GetEntities();

		for(var i = 0; i < steps; i++)
		{
			_engine.Simulate(world, dt);

			foreach(var entity in entities)
				sum += entity.v.Length;
		}

		return sum;
	}

	private async Task PreloadAsync(string resourcePath)
	{
		if(_cache.ContainsKey(resourcePath))
			return;

		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		_cache[resourcePath] = (world, dt);
	}

	#endregion
}