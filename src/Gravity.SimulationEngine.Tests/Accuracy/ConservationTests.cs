using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests.Accuracy;

[TestClass]
public sealed class ConservationTests
{
	#region Interface

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task AdaptiveLeapfrogConservesInvariantsTwoBody()
		=> await AssertConservationAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.TwoBodiesSimulation, 5000, 1e-3, 1e-12, 1e-12);

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task StandardConservesMomentumAngularTwoBody()
		=> await AssertConservationAsync(Factory.SimulationEngineType.Standard, ResourcePaths.TwoBodiesSimulation, 2000, 1e-2, 1e-12, 1e-12);

	[TestMethod]
	public async Task TestConservationAdaptiveAsync()
		=> await AssertConservationAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.TwoBodiesSimulation, 5000, 1e-3, 1e-12, 1e-12);

	#endregion

	#region Implementation

	private static double TotalKineticEnergy(IReadOnlyList<Body> bodies)
		=> bodies.Where(b => !b.IsAbsorbed).Sum(b => 0.5 * b.m * b.v.LengthSquared);

	private static double TotalPotentialEnergy(IReadOnlyList<Body> bodies)
	{
		var e = 0.0;

		for(var i = 0; i < bodies.Count; i++)
		{
			var bi = bodies[i];

			if(bi.IsAbsorbed)
				continue;

			for(var j = i + 1; j < bodies.Count; j++)
			{
				var bj = bodies[j];

				if(bj.IsAbsorbed)
					continue;

				var r = (bi.Position - bj.Position).Length;
				var rEff = Math.Max(r, 1e-12);
				e -= IWorld.G * bi.m * bj.m / rEff;
			}
		}

		return e;
	}

	private static (Vector3D P, Vector3D L) TotalMomentumAndAngularMomentum(IReadOnlyList<Body> bodies)
	{
		var p = Vector3D.Zero;
		var l = Vector3D.Zero;

		for(var i = 0; i < bodies.Count; i++)
		{
			var b = bodies[i];

			if(b.IsAbsorbed)
				continue;

			p += b.m * b.v;
			// Angular momentum L = r × p = r × (m * v)
			l += b.m * b.Position.Cross(b.v);
		}

		return (p, l);
	}

	private static async Task AssertConservationAsync(Factory.SimulationEngineType engineType,
													  string resourcePath,
													  int steps,
													  double relEnergyTol,
													  double relMomentumTol,
													  double relAngularTol,
													  bool debugOutput = false)
	{
		var engine = Factory.Create(engineType);
		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		world = world.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		var bodies = world.GetBodies();
		var e0 = TotalKineticEnergy(bodies) + TotalPotentialEnergy(bodies);
		(var p0, var l0) = TotalMomentumAndAngularMomentum(bodies);

		if(debugOutput)
		{
			Console.WriteLine($"Initial state:");
			Console.WriteLine($"  E0 = {e0:E6} (KE={TotalKineticEnergy(bodies):E6}, PE={TotalPotentialEnergy(bodies):E6})");
			Console.WriteLine($"  P0 = {p0}");
			Console.WriteLine($"  L0 = {l0}");
			foreach(var b in bodies)
				Console.WriteLine($"  Body: pos={b.Position}, v={b.v}, m={b.m}");
		}

		for(var s = 0; s < steps; s++)
		{
			engine.Simulate(world, viewport, dt);
			
			if(debugOutput && s % 500 == 0)
			{
				var eS = TotalKineticEnergy(bodies) + TotalPotentialEnergy(bodies);
				Console.WriteLine($"Step {s}: E = {eS:E6}, relE = {Math.Abs(eS - e0) / Math.Abs(e0):E6}");
			}
		}

		var eN = TotalKineticEnergy(bodies) + TotalPotentialEnergy(bodies);
		(var pN, var lN) = TotalMomentumAndAngularMomentum(bodies);

		if(debugOutput)
		{
			Console.WriteLine($"Final state:");
			Console.WriteLine($"  EN = {eN:E6}");
			Console.WriteLine($"  PN = {pN}");
			Console.WriteLine($"  LN = {lN}");
			foreach(var b in bodies)
				Console.WriteLine($"  Body: pos={b.Position}, v={b.v}");
		}

		const double eps = 1e-15;
		var relE = Math.Abs(eN - e0) / Math.Max(eps, Math.Abs(e0));
		var relP = (pN - p0).Length / Math.Max(eps, p0.Length);
		var relL = (lN - l0).Length / Math.Max(eps, l0.Length);

		if(debugOutput)
			Console.WriteLine($"Results: relE={relE:E6}, relP={relP:E6}, relL={relL:E6}");

		// Assert actual <= tolerance
		Assert.IsLessThanOrEqualTo(relEnergyTol, relE, $"Energy drift {relE} exceeds tolerance {relEnergyTol}");
		Assert.IsLessThanOrEqualTo(relMomentumTol, relP, $"Momentum drift {relP} exceeds tolerance {relMomentumTol}");
		Assert.IsLessThanOrEqualTo(relAngularTol, relL, $"Angular momentum drift {relL} exceeds tolerance {relAngularTol}");
	}

	#endregion
}