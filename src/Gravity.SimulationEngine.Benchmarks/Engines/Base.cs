using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Mock;

namespace Gravity.SimulationEngine.Benchmarks.Engines;

/// <summary>
/// Base class for all simulation benchmarks. Provides resource caching and engine management.
/// </summary>
public abstract class Base
{
	#region Fields

	private readonly Dictionary<string, (IWorld World, IViewport Viewport, TimeSpan Dt)> _cache = new();

	#endregion

	#region Interface

	/// <summary>
	/// Standard engine using O(n²) direct computation.
	/// </summary>
	protected ISimulationEngine Standard { get; private set; } = null!;

	/// <summary>
	/// Adaptive Barnes-Hut engine using O(n log n) tree-based computation.
	/// </summary>
	protected ISimulationEngine AdaptiveBarnesHut { get; private set; } = null!;

	/// <summary>
	/// Adaptive Particle-Mesh engine using O(N + Grid³ log Grid) FFT-based computation.
	/// </summary>
	protected ISimulationEngine AdaptiveParticleMesh { get; private set; } = null!;

	/// <summary>
	/// Adaptive Fast Multipole Method engine using O(N) multipole expansion.
	/// </summary>
	protected ISimulationEngine AdaptiveFastMultipole { get; private set; } = null!;

	[GlobalSetup]
	public async Task SetupAsync()
	{
		// Preload all resources
		await PreloadAsync(ResourcePaths.ThousandBodiesSimulation);
		await PreloadAsync(ResourcePaths.TenKBodiesSimulation);

		// Create all engines
		Standard = Factory.Create(Factory.SimulationEngineType.Standard);
		AdaptiveBarnesHut = Factory.Create(Factory.SimulationEngineType.AdaptiveBarnesHut);
		AdaptiveParticleMesh = Factory.Create(Factory.SimulationEngineType.AdaptiveParticleMesh);
		AdaptiveFastMultipole = Factory.Create(Factory.SimulationEngineType.AdaptiveFastMultipole);
	}

	#endregion

	#region Implementation

	[SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits")]
	protected double Run(ISimulationEngine engine, string resourcePath, int steps)
	{
		ArgumentNullException.ThrowIfNull(engine);

		if (!_cache.TryGetValue(resourcePath, out var entry))
		{
			PreloadAsync(resourcePath).GetAwaiter().GetResult();
			entry = _cache[resourcePath];
		}

		(var world, var viewport, var dt) = entry;
		world = world.CreateMock();
		var bodies = world.GetBodies();
		double sum = 0;

		for (var i = 0; i < steps; i++)
		{
			engine.Simulate(world, viewport, dt);
			sum += bodies.Sum(body => body.v.Length);
		}

		return sum;
	}

	private async Task PreloadAsync(string resourcePath)
	{
		if (_cache.ContainsKey(resourcePath))
			return;

		(var world, var viewport, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		_cache[resourcePath] = (world, viewport, dt);
	}

	#endregion
}
