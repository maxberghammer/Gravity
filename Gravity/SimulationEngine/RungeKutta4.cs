using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class RungeKutta4
	{
		public static async Task ProcessAsync(ISimulationEngine2 aSimulationEngine, Entity[] aEntities, TimeSpan aDeltaTime)
		{
			var state = aEntities.Select(e => new SimulationState(e.Position, e.v, e.m))
								 .ToArray();
			var h = aDeltaTime.TotalSeconds;

			var k1 = await aSimulationEngine.ProcessAsync(state);
			var tmp = state.Select((s, i) => new SimulationState(s.Position + h * 0.5d * k1[i].v,
																 s.v + h * 0.5d * k1[i].a,
																 s.m))
						   .ToArray();

			var k2 = await aSimulationEngine.ProcessAsync(tmp);
			tmp = state.Select((s, i) => new SimulationState(s.Position + h * 0.5d * k2[i].v,
															 s.v + h * 0.5d * k2[i].a,
															 s.m))
					   .ToArray();

			var k3 = await aSimulationEngine.ProcessAsync(tmp);
			tmp = state.Select((s, i) => new SimulationState(s.Position + h * k3[i].v,
															 s.v + h * k3[i].a,
															 s.m))
					   .ToArray();

			var k4 = await aSimulationEngine.ProcessAsync(tmp);

			for (var i = 0; i < aEntities.Length; i++)
			{
				aEntities[i].Position += h / 6.0d * (k1[i].v + 2.0d * (k2[i].v + k3[i].v) + k4[i].v);
				aEntities[i].v += h / 6.0d * (k1[i].a + 2.0d * (k2[i].a + k3[i].a) + k4[i].a);
			}
		}
	}
}
