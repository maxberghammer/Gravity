// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine.Integrators
{
	internal class NullIntegrator : IIntegrator
	{
		#region Implementation of IIntegrator

		/// <inheritdoc />
		async Task<Tuple<int, int>[]> IIntegrator.IntegrateAsync(Entity[] aEntities, TimeSpan aDeltaTime, Func<Entity[], Task<Tuple<int, int>[]>> aProcessFunc)
		{
			var collisions = await aProcessFunc(aEntities);

			await IntegrateAsync(aEntities, aDeltaTime);

			return collisions;
		}

		#endregion

		#region Implementation

		// ReSharper disable InconsistentNaming
		private static async Task IntegrateAsync(IReadOnlyCollection<Entity> aEntities, TimeSpan ah)
			// ReSharper restore InconsistentNaming
		{
			var chunkSize = aEntities.Count / Environment.ProcessorCount;

			await Task.WhenAll(aEntities.Chunked(chunkSize)
										.Select((chunk, _) => Task.Run(() =>
																	   {
																		   foreach (var entity in chunk)
																		   {
																			   entity.v += ah.TotalSeconds * entity.a;
																			   entity.Position += ah.TotalSeconds * entity.v;
																		   }
																	   })));
		}

		#endregion
	}
}