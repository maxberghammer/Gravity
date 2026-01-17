using System;
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
		=> await AssertConservationAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.TwoBodiesSimulation, 5000, 5e-4, 1e-10, 1e-10);

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task StandardConservesMomentumAngularTwoBody()
		=> await AssertConservationAsync(Factory.SimulationEngineType.Standard, ResourcePaths.TwoBodiesSimulation, 2000, 5e-2, 1e-9, 1e-9);

	[TestMethod]
	public async Task TestConservationAdaptiveAsync()
		=> await AssertConservationAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.TwoBodiesSimulation, 5000, 5e-4, 1e-10, 1e-10);

	#endregion

	#region Implementation

	private static double TotalKineticEnergy(Body[] bodies)
		=> bodies.Where(b => !b.IsAbsorbed).Sum(b => 0.5 * b.m * b.v.LengthSquared);

	private static double TotalPotentialEnergy(Body[] bodies)
	{
		var e = 0.0;

		for(var i = 0; i < bodies.Length; i++)
		{
			var bi = bodies[i];

			if(bi.IsAbsorbed)
				continue;

			for(var j = i + 1; j < bodies.Length; j++)
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

	private static (Vector3D P, double Lz) TotalMomentumAndAngularMomentum(Body[] bodies)
	{
		var p = Vector3D.Zero;
		var lz = 0.0;

		for(var i = 0; i < bodies.Length; i++)
		{
			var b = bodies[i];

			if(b.IsAbsorbed)
				continue;

			p += b.m * b.v;
			lz += b.m * (b.Position.X * b.v.Y - b.Position.Y * b.v.X);
		}

		return (p, lz);
	}

	private static async Task AssertConservationAsync(Factory.SimulationEngineType engineType,
													  string resourcePath,
													  int steps,
													  double relEnergyTol,
													  double relMomentumTol,
													  double relAngularTol)
	{
		var engine = Factory.Create(engineType);
		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		world = world.CreateMock();

		var bodies = world.GetBodies();
		var e0 = TotalKineticEnergy(bodies) + TotalPotentialEnergy(bodies);
		(var p0, var l0) = TotalMomentumAndAngularMomentum(bodies);

		for(var s = 0; s < steps; s++)
			engine.Simulate(world, dt);

		var eN = TotalKineticEnergy(bodies) + TotalPotentialEnergy(bodies);
		(var pN, var lN) = TotalMomentumAndAngularMomentum(bodies);

		const double eps = 1e-15;
		var relE = Math.Abs(eN - e0) / Math.Max(eps, Math.Abs(e0));
		var relP = (pN - p0).Length / Math.Max(eps, p0.Length);
		var relL = Math.Abs(lN - l0) / Math.Max(eps, Math.Abs(l0));

		// Use (tolerance, actual) order for Assert.IsLessThanOrEqualTo
		Assert.IsLessThanOrEqualTo(relEnergyTol, relE, $"Energy drift {relE} exceeds tolerance {relEnergyTol}");
		Assert.IsLessThanOrEqualTo(relMomentumTol, relP, $"Momentum drift {relP} exceeds tolerance {relMomentumTol}");
		Assert.IsLessThanOrEqualTo(relAngularTol, relL, $"Angular momentum drift {relL} exceeds tolerance {relAngularTol}");
	}

	#endregion
}