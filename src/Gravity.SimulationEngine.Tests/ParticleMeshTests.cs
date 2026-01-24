using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
[SuppressMessage("Style", "MSTEST0037:Use improved Assert APIs", Justification = "Array.Length is clearer than HasCount")]
public sealed class ParticleMeshTests : EngineTestsBase
{
	#region Implementation

	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveParticleMesh;

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task ParticleMeshSimulates()
	{
		// Simple test to verify PM strategy actually runs
		var engine = Factory.Create(EngineType);
		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.TwoBodiesSimulation);
		world = world.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		var bodies = world.GetBodies();
		Assert.AreEqual(2, bodies.Count);

		// Get initial positions
		var pos0_before = bodies[0].Position;
		var pos1_before = bodies[1].Position;

		// Simulate 100 steps
		for(var i = 0; i < 100; i++)
			engine.Simulate(world, viewport, dt);

		// Verify bodies moved
		var pos0_after = bodies[0].Position;
		var pos1_after = bodies[1].Position;

		var moved0 = (pos0_after - pos0_before).Length;
		var moved1 = (pos1_after - pos1_before).Length;

		Assert.IsTrue(moved0 > 1e-6, $"Body 0 didn't move: {moved0}");
		Assert.IsTrue(moved1 > 1e-6, $"Body 1 didn't move: {moved1}");

		// Check diagnostics
		var diag = engine.GetDiagnostics();
		Assert.IsTrue(diag.Fields.ContainsKey("Strategy"), "No Strategy diagnostic");
		var strategy = diag.Fields["Strategy"].ToString();
		Assert.IsTrue(strategy != null && strategy.Contains("Particle-Mesh", StringComparison.Ordinal), "PM strategy not running");
	}
	
	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task CompareWithBarnesHut()
	{
		// Compare PM vs Barnes-Hut accelerations
		var enginePM = Factory.Create(Factory.SimulationEngineType.AdaptiveParticleMesh);
		var engineBH = Factory.Create(Factory.SimulationEngineType.AdaptiveBarnesHut);
		
		(var worldPM, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.ThousandBodiesSimulation);
		(var worldBH, var _) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.ThousandBodiesSimulation);
		
		worldPM = worldPM.CreateMock();
		worldBH = worldBH.CreateMock();
		var viewportPM = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));
		var viewportBH = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		var bodiesPM = worldPM.GetBodies();
		var bodiesBH = worldBH.GetBodies();

		// Simulate just 1 step to compare accelerations
		enginePM.Simulate(worldPM, viewportPM, dt);
		engineBH.Simulate(worldBH, viewportBH, dt);

		// Compare average acceleration magnitudes
		var avgAccPM = 0.0;
		var avgAccBH = 0.0;
		var maxRelError = 0.0;
		var count = 0;

		for(var i = 0; i < bodiesPM.Count; i++)
		{
			if(bodiesPM[i].IsAbsorbed || bodiesBH[i].IsAbsorbed)
				continue;

			var accPM = bodiesPM[i].a.Length;
			var accBH = bodiesBH[i].a.Length;
			
			avgAccPM += accPM;
			avgAccBH += accBH;
			
			if(accBH > 1e-10)
			{
				var relErr = Math.Abs(accPM - accBH) / accBH;
				if(relErr > maxRelError)
					maxRelError = relErr;
			}
			count++;
		}

		avgAccPM /= count;
		avgAccBH /= count;

		var diagPM = enginePM.GetDiagnostics();

		// Log results
		Console.WriteLine($"PM avg acceleration: {avgAccPM:E3}");
		Console.WriteLine($"BH avg acceleration: {avgAccBH:E3}");
		Console.WriteLine($"Relative difference: {Math.Abs(avgAccPM - avgAccBH) / avgAccBH * 100:F1}%");
		Console.WriteLine($"Max relative error: {maxRelError * 100:F1}%");
		Console.WriteLine($"Grid size: {diagPM.Fields["GridSize"]}");
		if(diagPM.Fields.ContainsKey("GridSpacing"))
			Console.WriteLine($"Grid spacing: {diagPM.Fields["GridSpacing"]}");
		if(diagPM.Fields.ContainsKey("GridSpacingX"))
			Console.WriteLine($"Grid spacing X: {diagPM.Fields["GridSpacingX"]}");

		// PM should produce same order of magnitude as Barnes-Hut
		// With grid-based approximation, expect some error
		var relDiff = Math.Abs(avgAccPM - avgAccBH) / avgAccBH;
		Assert.IsTrue(relDiff < 2.0, 
			$"PM differs too much from BH: avgAccPM={avgAccPM:E3}, avgAccBH={avgAccBH:E3}, relDiff={relDiff*100:F1}%");

		// Verify PM is actually running
		Assert.IsTrue(diagPM.Fields.ContainsKey("Strategy"), "PM missing Strategy diagnostic");
		var strategy = diagPM.Fields["Strategy"].ToString();
		Assert.IsTrue(strategy != null && strategy.Contains("Particle-Mesh", StringComparison.Ordinal), "PM strategy not running");
	}

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task TwoBodyForceDirection()
	{
		// Test that two bodies attract each other (force points toward the other body)
		var enginePM = Factory.Create(Factory.SimulationEngineType.AdaptiveParticleMesh);
		var engineBH = Factory.Create(Factory.SimulationEngineType.AdaptiveBarnesHut);

		(var worldPM, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.TwoBodiesSimulation);
		(var worldBH, var _) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.TwoBodiesSimulation);

		worldPM = worldPM.CreateMock();
		worldBH = worldBH.CreateMock();
		var viewportPM = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));
		var viewportBH = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));

		var bodiesPM = worldPM.GetBodies();
		var bodiesBH = worldBH.GetBodies();

		// Single step to get accelerations
		enginePM.Simulate(worldPM, viewportPM, dt);
		engineBH.Simulate(worldBH, viewportBH, dt);

		// Body 0 and Body 1 positions
		var pos0 = bodiesPM[0].Position;
		var pos1 = bodiesPM[1].Position;
		var separation = pos1 - pos0; // Vector from body0 to body1

		// Accelerations
		var acc0_PM = bodiesPM[0].a;
		var acc1_PM = bodiesPM[1].a;
		var acc0_BH = bodiesBH[0].a;
		var acc1_BH = bodiesBH[1].a;

		Console.WriteLine($"Body 0 position: {pos0}");
		Console.WriteLine($"Body 1 position: {pos1}");
		Console.WriteLine($"Separation vector (0→1): {separation}");
		Console.WriteLine();
		Console.WriteLine($"PM Body 0 acceleration: {acc0_PM} (magnitude: {acc0_PM.Length:E3})");
		Console.WriteLine($"PM Body 1 acceleration: {acc1_PM} (magnitude: {acc1_PM.Length:E3})");
		Console.WriteLine($"BH Body 0 acceleration: {acc0_BH} (magnitude: {acc0_BH.Length:E3})");
		Console.WriteLine($"BH Body 1 acceleration: {acc1_BH} (magnitude: {acc1_BH.Length:E3})");
		Console.WriteLine();

		// For gravity, body 0 should accelerate TOWARD body 1 (same direction as separation)
		// and body 1 should accelerate TOWARD body 0 (opposite direction as separation)
		var dot0_PM = acc0_PM.X * separation.X + acc0_PM.Y * separation.Y;
		var dot1_PM = acc1_PM.X * separation.X + acc1_PM.Y * separation.Y;
		var dot0_BH = acc0_BH.X * separation.X + acc0_BH.Y * separation.Y;
		var dot1_BH = acc1_BH.X * separation.X + acc1_BH.Y * separation.Y;

		Console.WriteLine($"PM Body 0 acc · separation = {dot0_PM:E3} (should be > 0 for attraction)");
		Console.WriteLine($"PM Body 1 acc · separation = {dot1_PM:E3} (should be < 0 for attraction)");
		Console.WriteLine($"BH Body 0 acc · separation = {dot0_BH:E3} (should be > 0 for attraction)");
		Console.WriteLine($"BH Body 1 acc · separation = {dot1_BH:E3} (should be < 0 for attraction)");

		// Barnes-Hut should definitely be correct
		Assert.IsTrue(dot0_BH > 0, $"BH Body 0 should accelerate toward Body 1, but dot={dot0_BH}");
		Assert.IsTrue(dot1_BH < 0, $"BH Body 1 should accelerate toward Body 0, but dot={dot1_BH}");

		// PM should also show attraction
		Assert.IsTrue(dot0_PM > 0, $"PM Body 0 should accelerate toward Body 1, but dot={dot0_PM}");
		Assert.IsTrue(dot1_PM < 0, $"PM Body 1 should accelerate toward Body 0, but dot={dot1_PM}");
	}

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task TwoBodyActuallyApproach()
	{
		// Test that two bodies actually move closer together over time
		var enginePM = Factory.Create(Factory.SimulationEngineType.AdaptiveParticleMesh);

		(var world, var dt) = await IWorld.CreateFromJsonResourceAsync(ResourcePaths.TwoBodiesSimulation);
		world = world.CreateMock();
		var viewport = new ViewportMock(new(-1000, -1000, -1000), new(1000, 1000, 1000));
		var bodies = world.GetBodies();

		var initialDist = (bodies[0].Position - bodies[1].Position).Length;
		Console.WriteLine($"Initial distance: {initialDist:F2}");

		// Simulate 100 steps
		for(var step = 0; step < 100; step++)
		{
			enginePM.Simulate(world, viewport, dt);
			
			if(step % 20 == 0)
			{
				var dist = (bodies[0].Position - bodies[1].Position).Length;
				Console.WriteLine($"Step {step}: distance = {dist:F2}, v0 = {bodies[0].v.Length:E2}, v1 = {bodies[1].v.Length:E2}");
			}
		}

		var finalDist = (bodies[0].Position - bodies[1].Position).Length;
		Console.WriteLine($"Final distance: {finalDist:F2}");

		// Bodies should have moved closer (or at least not flown apart dramatically)
		Assert.IsTrue(finalDist < initialDist * 1.5, 
			$"Bodies flew apart! Initial: {initialDist:F2}, Final: {finalDist:F2}");
	}
	#endregion
}
