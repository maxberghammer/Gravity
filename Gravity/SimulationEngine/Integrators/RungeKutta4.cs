// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine.Integrators
{
	internal class RungeKutta4 : IIntegrator
	{
		#region Implementation of IIntegrator

		async Task<Tuple<int, int>[]> IIntegrator.IntegrateAsync(Entity[] aEntities, TimeSpan aDeltaTime, Func<Entity[], Task<Tuple<int, int>[]>> aProcessFunc)
		{
			var h = aDeltaTime;

			// k1
			var k1 = aEntities.Select(e => new Entity(e))
							  .ToArray();
			var k1Collisions = await aProcessFunc(k1);

			await IntegrateAsync(k1, aEntities, 0.5d * h);

			// k2
			var k2 = k1.Select(e => new Entity(e))
					   .ToArray();
			var k2Collisions = await aProcessFunc(k2);

			await IntegrateAsync(k2, aEntities, 0.5d * h);

			// k3
			var k3 = k2.Select(e => new Entity(e))
					   .ToArray();
			var k3Collisions = await aProcessFunc(k3);

			await IntegrateAsync(k3, aEntities, h);

			// k4
			var k4 = k3.Select(e => new Entity(e))
					   .ToArray();
			var k4Collisions = await aProcessFunc(k4);

			await IntegrateAsync(aEntities, k1, k2, k3, k4, h);

			return k1Collisions.Union(k2Collisions)
							   .Union(k3Collisions)
							   .Union(k4Collisions)
							   .ToArray();
		}

		#endregion

		#region Implementation

		// ReSharper disable InconsistentNaming
		private static async Task IntegrateAsync(IEnumerable<Entity> ak, IReadOnlyList<Entity> aEntities, TimeSpan ah)
			// ReSharper restore InconsistentNaming
		{
			var chunkSize = aEntities.Count / Environment.ProcessorCount;

			await Task.WhenAll(ak.Chunked(chunkSize)
								 .Select((chunk, chunkIndex) => Task.Run(() =>
																		 {
																			 for (var i = 0; i < chunk.Length; i++)
																			 {
																				 var k = chunk[i];
																				 var entity = aEntities[chunkIndex * chunkSize + i];

																				 k.v = entity.v + ah.TotalSeconds * k.a;
																				 k.Position = entity.Position + ah.TotalSeconds * k.v;
																			 }
																		 })));
		}

		// ReSharper disable InconsistentNaming
		private static async Task IntegrateAsync(IReadOnlyCollection<Entity> aEntities, IReadOnlyList<Entity> ak1, IReadOnlyList<Entity> ak2,
												 IReadOnlyList<Entity> ak3, IReadOnlyList<Entity> ak4, TimeSpan ah)
			// ReSharper restore InconsistentNaming
		{
			var chunkSize = aEntities.Count / Environment.ProcessorCount;
			var h = 1.0d / 6.0d * ah.TotalSeconds;

			await Task.WhenAll(aEntities.Chunked(chunkSize)
										.Select((chunk, chunkIndex) => Task.Run(() =>
																				{
																					for (var i = 0; i < chunk.Length; i++)
																					{
																						var k1 = ak1[chunkIndex * chunkSize + i];
																						var k2 = ak2[chunkIndex * chunkSize + i];
																						var k3 = ak3[chunkIndex * chunkSize + i];
																						var k4 = ak4[chunkIndex * chunkSize + i];
																						var entity = chunk[i];

																						entity.v += h * (k1.a + 2.0d * (k2.a + k3.a) + k4.a);
																						entity.Position += h * (k1.v + 2.0d * (k2.v + k3.v) + k4.v);
																					}
																				})));
		}

		#endregion
	}
}