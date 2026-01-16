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

		var bodies = world.GetBodies();
		Assert.AreEqual(2, bodies.Length);

		// Get initial positions
		var pos0_before = bodies[0].Position;
		var pos1_before = bodies[1].Position;

		// Simulate 100 steps
		for(var i = 0; i < 100; i++)
			engine.Simulate(world, dt);

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

		var bodiesPM = worldPM.GetBodies();
		var bodiesBH = worldBH.GetBodies();

		// Simulate just 1 step to compare accelerations
		enginePM.Simulate(worldPM, dt);
		engineBH.Simulate(worldBH, dt);

		// Compare average acceleration magnitudes
		var avgAccPM = 0.0;
		var avgAccBH = 0.0;
		var maxRelError = 0.0;
		var count = 0;

		for(var i = 0; i < bodiesPM.Length; i++)
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
	#endregion
}
