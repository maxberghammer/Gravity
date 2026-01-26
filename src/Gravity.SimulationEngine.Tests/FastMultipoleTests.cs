using System;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
public class FastMultipoleTests
{
	[TestMethod]
	public async Task FmmSimulates()
	{
		var engine = Factory.Create(Factory.SimulationEngineType.AdaptiveFastMultipole);
		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.ThousandBodiesSimulation);
		
		world = world.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		engine.Simulate(world, viewport, dt);
		
		var diag = engine.GetDiagnostics();
		var strategy = diag.Fields["Strategy"].ToString();
		
		Assert.IsTrue(strategy != null && strategy.Contains("FMM", StringComparison.Ordinal), "FMM strategy not running");
	}

	[TestMethod]
	public async Task CompareWithBarnesHut()
	{
		var engineFMM = Factory.Create(Factory.SimulationEngineType.AdaptiveFastMultipole);
		var engineBH = Factory.Create(Factory.SimulationEngineType.AdaptiveBarnesHut);
		
		(var worldFMM, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.ThousandBodiesSimulation);
		(var worldBH, var _) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.ThousandBodiesSimulation);
		
		worldFMM = worldFMM.CreateMock();
		worldBH = worldBH.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		var bodiesFMM = worldFMM.GetBodies();
		var bodiesBH = worldBH.GetBodies();

		engineFMM.Simulate(worldFMM, viewport, dt);
		engineBH.Simulate(worldBH, viewport, dt);

		// Compare accelerations
		var totalDiff = 0.0;
		var totalMag = 0.0;
		var count = 0;

		for (var i = 0; i < bodiesFMM.Count; i++)
		{
			if (bodiesFMM[i].IsAbsorbed || bodiesBH[i].IsAbsorbed)
				continue;

			var accFMM = bodiesFMM[i].a;
			var accBH = bodiesBH[i].a;
			
			totalDiff += (accFMM - accBH).Length;
			totalMag += accBH.Length;
			count++;
		}

		var avgRelErr = totalDiff / totalMag;
		Console.WriteLine($"FMM vs BH: avg relative error = {avgRelErr * 100:F2}%");
		
		// FMM with quadrupole should be fairly accurate (2-10% is typical)
		Assert.IsLessThanOrEqualTo(0.5, avgRelErr, $"FMM differs too much from BH: {avgRelErr * 100:F1}%");
	}

	[TestMethod]
	public async Task TwoBodyForceDirection()
	{
		var engine = Factory.Create(Factory.SimulationEngineType.AdaptiveFastMultipole);

		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.TwoBodiesSimulation);
		world = world.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));


		var bodies = world.GetBodies();
		var pos0 = bodies[0].Position;
		var pos1 = bodies[1].Position;

		engine.Simulate(world, viewport, dt);

		// Bodies should accelerate toward each other
		var acc0 = bodies[0].a;
		var acc1 = bodies[1].a;

		var dir01 = pos1 - pos0;
		var dir10 = pos0 - pos1;

		// Dot product should be positive (same direction)
		var dot0 = acc0.X * dir01.X + acc0.Y * dir01.Y + acc0.Z * dir01.Z;
		var dot1 = acc1.X * dir10.X + acc1.Y * dir10.Y + acc1.Z * dir10.Z;

		// IsGreaterThan(lowerBound, value) - value should be greater than lowerBound
		Assert.IsGreaterThan(0.0, dot0, $"Body 0 should accelerate toward Body 1");
		Assert.IsGreaterThan(0.0, dot1, $"Body 1 should accelerate toward Body 0");
	}
}
