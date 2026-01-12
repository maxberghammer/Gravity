using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests.Common;

public abstract class EngineTestsBase
{
	#region Implementation

	protected abstract Factory.SimulationEngineType EngineType { get; }

	protected async Task RunAsync(string jsonResourcePath, int steps)
	{
		var engine = Factory.Create(EngineType);
		(var _, var entities, var deltaTime) = await WorldMock.CreateFromJsonResourceAsync(jsonResourcePath);

		for(var s = 0; s < steps; s++)
			engine.Simulate(entities, deltaTime);

		foreach(var entity in entities)
		{
			Assert.IsFalse(double.IsNaN(entity.Position.X) || double.IsNaN(entity.Position.Y));
			Assert.IsFalse(double.IsInfinity(entity.Position.X) || double.IsInfinity(entity.Position.Y));
		}
	}

	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task Run1000Steps2BodyAsync()
		=> await RunAsync(ResourcePaths.TwoBodiesSimulation, 1000);

	#endregion
}