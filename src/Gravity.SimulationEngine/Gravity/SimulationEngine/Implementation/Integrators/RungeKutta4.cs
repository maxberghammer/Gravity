// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class RungeKutta4 : IIntegrator
{
	#region Implementation of IIntegrator

	async Task<Tuple<int, int>[]> IIntegrator.IntegrateAsync(Entity[] entities, TimeSpan deltaTime, Func<Entity[], Task<Tuple<int, int>[]>> processFunc)
	{
		var h = deltaTime;

		// k1
		var k1 = entities.Select(e => new Entity(e))
						 .ToArray();
		var k1Collisions = await processFunc(k1);

		await IntegrateAsync(k1, entities, 0.5d * h);

		// k2
		var k2 = k1.Select(e => new Entity(e))
				   .ToArray();
		var k2Collisions = await processFunc(k2);

		await IntegrateAsync(k2, entities, 0.5d * h);

		// k3
		var k3 = k2.Select(e => new Entity(e))
				   .ToArray();
		var k3Collisions = await processFunc(k3);

		await IntegrateAsync(k3, entities, h);

		// k4
		var k4 = k3.Select(e => new Entity(e))
				   .ToArray();
		var k4Collisions = await processFunc(k4);

		await IntegrateAsync(entities, k1, k2, k3, k4, h);

		return k1Collisions.Union(k2Collisions)
						   .Union(k3Collisions)
						   .Union(k4Collisions)
						   .ToArray();
	}

	#endregion

	#region Implementation

	private static async Task IntegrateAsync(IEnumerable<Entity> ks, Entity[] entities, TimeSpan h)
	{
		var chunkSize = IWorld.GetPreferredChunkSize(entities);

		await Task.WhenAll(ks.Chunked(chunkSize)
							 .Select((chunk, chunkIndex) => Task.Run(() =>
																	 {
																		 for(var i = 0; i < chunk.Length; i++)
																		 {
																			 var k = chunk[i];
																			 var entity = entities[chunkIndex * chunkSize + i];

																			 k.v = entity.v + h.TotalSeconds * k.a;
																			 k.Position = entity.Position + h.TotalSeconds * k.v;
																		 }
																	 })));
	}

	private static async Task IntegrateAsync(IReadOnlyCollection<Entity> entities,
											 Entity[] k1s,
											 Entity[] k2s,
											 Entity[] k3s,
											 Entity[] k4s,
											 TimeSpan h)
	{
		var chunkSize = IWorld.GetPreferredChunkSize(entities);
		var dh = 1.0d / 6.0d * h.TotalSeconds;

		await Task.WhenAll(entities.Chunked(chunkSize)
								   .Select((chunk, chunkIndex) => Task.Run(() =>
																		   {
																			   for(var i = 0; i < chunk.Length; i++)
																			   {
																				   var k1 = k1s[chunkIndex * chunkSize + i];
																				   var k2 = k2s[chunkIndex * chunkSize + i];
																				   var k3 = k3s[chunkIndex * chunkSize + i];
																				   var k4 = k4s[chunkIndex * chunkSize + i];
																				   var entity = chunk[i];

																				   entity.v += dh * (k1.a + 2.0d * (k2.a + k3.a) + k4.a);
																				   entity.Position += dh * (k1.v + 2.0d * (k2.v + k3.v) + k4.v);
																			   }
																		   })));
	}

	#endregion
}