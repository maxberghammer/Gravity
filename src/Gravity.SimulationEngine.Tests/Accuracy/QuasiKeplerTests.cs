using System;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests.Accuracy;

/// <summary>
/// Quasi-Kepler tests for solar systems with one dominant central body.
/// Tests orbit stability and period accuracy for multiple planets around a star.
/// </summary>
[TestClass]
public sealed class QuasiKeplerTests
{
	#region Interface

	// Solar system: 1 star (1e15 kg) + 10 planets (3e4-5e5 kg)
	// Planets are in circular orbits with correct v = √(GM/r) velocities
	// Mass ratio: planets are 2-30 million times lighter than star → minimal perturbations

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task DirectMaintainsStableOrbits()
		=> await AssertOrbitStabilityAsync(Factory.SimulationEngineType.Direct, ResourcePaths.SolarSystemSimulation, 5000,
										   0.05, 0.05);

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AdaptiveBarnesHutMaintainsStableOrbits()
		=> await AssertOrbitStabilityAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.SolarSystemSimulation, 5000,
										   0.1, 0.1);

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ParticleMeshMaintainsStableOrbits()
		=> await AssertOrbitStabilityAsync(Factory.SimulationEngineType.AdaptiveParticleMesh, ResourcePaths.SolarSystemSimulation, 5000,
										   0.15, 0.15);

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task FastMultipoleMaintainsStableOrbits()
	=> await AssertOrbitStabilityAsync(Factory.SimulationEngineType.AdaptiveFastMultipole, ResourcePaths.SolarSystemSimulation, 5000,
				0.12, 0.12);

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task HierarchicalBlockDirectMaintainsStableOrbits()
	=> await AssertOrbitStabilityAsync(Factory.SimulationEngineType.HierarchicalBlockDirect, ResourcePaths.SolarSystemSimulation, 5000,
				0.1, 0.1);

	#endregion

	#region Implementation

	private static async Task AssertOrbitStabilityAsync(Factory.SimulationEngineType engineType,
														string resourcePath,
														int steps,
														double maxRadiusDrift,
														double maxPeriodDrift)
	{
		var engine = Factory.Create(engineType);
		(var world, var viewport, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		world = world.CreateMock();

		var bodies = world.GetBodies();

		// Identify central star (heaviest body)
		var star = bodies.OrderByDescending(b => b.m).First();
		var planets = bodies.Where(b => b != star).ToList();

		// Record initial orbital parameters for each planet
		var initialOrbits = planets.Select(p => new
												{
													Planet = p,
													InitialRadius = (p.Position - star.Position).Length,
													InitialVelocity = p.v.Length
												}).ToList();

		// Simulate
		for(var s = 0; s < steps; s++)
			engine.Simulate(world, viewport, dt);

		// Test 0: All planets should still exist (none absorbed/removed)
		var currentPlanets = bodies.Where(b => b != star).ToList();
		Assert.HasCount(planets.Count, currentPlanets,
						$"{planets.Count - currentPlanets.Count} planet(s) were removed/absorbed - orbits are unstable!");

		// Check orbit stability for each planet
		foreach(var orbit in initialOrbits)
		{
			var finalRadius = (orbit.Planet.Position - star.Position).Length;
			var finalVelocity = orbit.Planet.v.Length;

			// Test 1: Radius shouldn't drift significantly
			var radiusDrift = Math.Abs(finalRadius - orbit.InitialRadius) / orbit.InitialRadius;
			Assert.IsLessThan(maxRadiusDrift, radiusDrift,
							  $"Planet orbit radius drifted too much: {radiusDrift * 100:F1}% (initial={orbit.InitialRadius:F1}, final={finalRadius:F1})");

			// Test 2: Velocity shouldn't drift significantly (for circular orbits v² = GM/r)
			var velocityDrift = Math.Abs(finalVelocity - orbit.InitialVelocity) / orbit.InitialVelocity;
			Assert.IsLessThan(maxPeriodDrift, velocityDrift,
							  $"Planet orbital velocity drifted too much: {velocityDrift * 100:F1}% (initial={orbit.InitialVelocity:F3}, final={finalVelocity:F3})");

			// Test 3: Planet is still orbiting (not flying away)
			Assert.IsLessThan(1000.0, finalRadius,
							  $"Planet flew away: radius={finalRadius:F1} > 1000");
		}

		// Test 4: No planets escaped
		var escapedCount = currentPlanets.Count(p => (p.Position - star.Position).Length > 1000);
		Assert.AreEqual(0, escapedCount, $"{escapedCount} planet(s) escaped from the system");

		// Test 5: Star should stay approximately stationary (momentum conservation)
		var starDisplacement = star.Position.Length;
		Assert.IsLessThan(50.0, starDisplacement,
						  $"Star moved too much: {starDisplacement:F1} (should stay near origin due to momentum conservation)");
	}

	#endregion
}