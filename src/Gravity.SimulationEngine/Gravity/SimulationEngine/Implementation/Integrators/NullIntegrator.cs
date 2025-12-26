// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class NullIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	/// <inheritdoc/>
	async Task<Tuple<int, int>[]> IIntegrator.IntegrateAsync(Entity[] entities, TimeSpan deltaTime, Func<Entity[], Task<Tuple<int, int>[]>> processFunc)
	{
		var collisions = await processFunc(entities);

		await IntegrateAsync(entities, deltaTime);

		return collisions;
	}

	#endregion

	#region Implementation

	private static async Task IntegrateAsync(IReadOnlyCollection<Entity> entities, TimeSpan h)
		=> await Task.WhenAll(entities.Chunked(IWorld.GetPreferredChunkSize(entities))
									  .Select((chunk, _) => Task.Run(() =>
																	 {
																		 foreach(var entity in chunk)
																		 {
																			 entity.v += h.TotalSeconds * entity.a;
																			 entity.Position += h.TotalSeconds * entity.v;
																		 }
																	 })));

	#endregion
}