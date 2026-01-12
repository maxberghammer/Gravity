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

	private readonly Dictionary<string, (IWorld World, Entity[] Baseline, TimeSpan Dt)> _cache = new();
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

		(var world, var baseline, var dt) = entry;
		var entities = CloneEntities(baseline, world);

		double sum = 0;

		for(var i = 0; i < steps; i++)
		{
			_engine.Simulate(entities, dt);

			foreach(var entity in entities)
				sum += entity.v.Length;
		}

		return sum;
	}

	private static Entity[] CloneEntities(Entity[] baseline, IWorld world)
	{
		var copy = new Entity[baseline.Length];

		for(var i = 0; i < baseline.Length; i++)
		{
			var e = baseline[i];
			copy[i] = new(new(e.Position.X, e.Position.Y),
						  e.r,
						  e.m,
						  new(e.v.X, e.v.Y),
						  Vector2D.Zero,
						  world,
						  e.Fill,
						  e.Stroke,
						  e.StrokeWidth);
		}

		return copy;
	}

	private async Task PreloadAsync(string resourcePath)
	{
		if(_cache.ContainsKey(resourcePath))
			return;

		(var world, var entities, var dt) = await WorldMock.CreateFromJsonResourceAsync(resourcePath);
		_cache[resourcePath] = (world, entities, dt);
	}

	#endregion
}