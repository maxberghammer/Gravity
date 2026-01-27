using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests.Common;

public abstract class EngineTestsBase
{
	#region Interface

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task Run1000Steps2BodyAsync()
		=> await RunAsync(ResourcePaths.TwoBodiesSimulation, 1000);

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task Run1000Steps1000BodyAsync()
		=> await RunAsync(ResourcePaths.ThousandBodiesSimulation, 1000);

	#endregion

	#region Implementation

	protected abstract Factory.SimulationEngineType EngineType { get; }

	protected async Task RunAsync(string jsonResourcePath, int steps)
	{
		var engine = Factory.Create(EngineType);
		(var world, var viewport, var deltaTime) = await IWorld.CreateFromJsonResourceAsync(jsonResourcePath);

		world = world.CreateMock();
		var bodies = world.GetBodies();

		// Store initial state
		var initialPositions = bodies.Select(b => b.Position).ToArray();
		var initialActiveCount = bodies.Count;
		var initialEnergy = ComputeTotalEnergy(bodies);

		// Run simulation
		for(var s = 0; s < steps; s++)
			engine.Simulate(world, viewport, deltaTime);

		// 1. No NaN or Infinity in positions
		foreach(var body in bodies)
		{
			Assert.IsFalse(double.IsNaN(body.Position.X) || double.IsNaN(body.Position.Y) || double.IsNaN(body.Position.Z),
						   $"Body position contains NaN: {body.Position}");
			Assert.IsFalse(double.IsInfinity(body.Position.X) || double.IsInfinity(body.Position.Y) || double.IsInfinity(body.Position.Z),
						   $"Body position contains Infinity: {body.Position}");
		}

		// 2. No NaN or Infinity in velocities
		foreach(var body in bodies)
		{
			Assert.IsFalse(double.IsNaN(body.v.X) || double.IsNaN(body.v.Y) || double.IsNaN(body.v.Z),
						   $"Body velocity contains NaN: {body.v}");
			Assert.IsFalse(double.IsInfinity(body.v.X) || double.IsInfinity(body.v.Y) || double.IsInfinity(body.v.Z),
						   $"Body velocity contains Infinity: {body.v}");
		}

		// 3. Simulation actually happened: at least one body moved
		var anyMoved = false;

		for(var i = 0; i < Math.Min(bodies.Count, initialPositions.Length); i++)
		{
			var displacement = (bodies[i].Position - initialPositions[i]).Length;

			if(displacement > 1e-10)
			{
				anyMoved = true;

				break;
			}
		}

		Assert.IsTrue(anyMoved, "No body moved - simulation may not be running");

		// 4. At least one body was accelerated
		if(bodies.Count > 1) // Need at least 2 bodies for gravitational acceleration
		{
			var anyAccelerated = bodies.Any(b => b.a.Length > 1e-20);
			Assert.IsTrue(anyAccelerated, "No body was accelerated - gravity may not be computed");
		}

		// 5. Energy didn't explode (rough check: < 100x initial energy)
		var finalEnergy = ComputeTotalEnergy(bodies);
		var energyRatio = Math.Abs(finalEnergy) / Math.Max(1e-10, Math.Abs(initialEnergy));
		Assert.IsLessThan(100.0, energyRatio, $"Energy exploded: initial={initialEnergy:E3}, final={finalEnergy:E3}, ratio={energyRatio:F1}x");

		// 6. Positions stayed in reasonable bounds (not flying to infinity)
		const double reasonableBound = 1e10; // 10 billion meters

		foreach(var body in bodies)
		{
			var distance = body.Position.Length;
			Assert.IsLessThan(reasonableBound, distance,
							  $"Body flew too far: {distance:E3} m (> {reasonableBound:E3} m)");
		}

		// 7. No spontaneous removal (bodies don't disappear - they're removed from list when absorbed)
		// Allow up to 50% loss (some test scenarios may have collisions)
		var finalCount = bodies.Count;
		var lossRate = (initialActiveCount - finalCount) / (double)initialActiveCount;
		Assert.IsLessThan(0.5, lossRate,
						  $"Too many bodies removed: {initialActiveCount} -> {finalCount} ({lossRate * 100:F0}% lost)");
	}

	private static double ComputeTotalEnergy(IReadOnlyList<Body> bodies)
	{
		// Kinetic energy
		var ke = 0.0;
		foreach(var b in bodies)
			ke += 0.5 * b.m * b.v.LengthSquared;

		// Potential energy
		var pe = 0.0;

		for(var i = 0; i < bodies.Count; i++)
		{
			for(var j = i + 1; j < bodies.Count; j++)
			{
				var r = (bodies[i].Position - bodies[j].Position).Length;
				var rEff = Math.Max(r, 1e-12);
				pe -= IWorld.G * bodies[i].m * bodies[j].m / rEff;
			}
		}

		return ke + pe;
	}

	#endregion
}