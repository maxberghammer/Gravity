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
	#endregion
}
