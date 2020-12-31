using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class StandardSimulationEngine : SimulationEngine, ISimulationEngine
	{
		#region Fields

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mPositionByEntityId = new ConcurrentDictionary<int, Vector>();

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mvByEntityId = new ConcurrentDictionary<int, Vector>();

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mgByEntityId = new ConcurrentDictionary<int, Vector>();

		#endregion

		#region Implementation of ISimulationEngine

		/// <inheritdoc />
		async Task ISimulationEngine.SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime)
		{
			// Physik anwenden
			await ApplyPhysicsAsync(aEntities.Where(e => !e.IsAbsorbed)
											 .ToArray());

			// Objekte updaten
			foreach (var entity in aEntities)
				Update(entity,
					   mPositionByEntityId.TryGetValue(entity.Id, out var position)
						   ? position
						   : (Vector?)null,
					   mvByEntityId.TryGetValue(entity.Id, out var v)
						   ? v
						   : (Vector?)null,
					   mgByEntityId.TryGetValue(entity.Id, out var g)
						   ? g
						   : (Vector?)null,
					   aDeltaTime);
		}

		#endregion

		#region Implementation

		private void ApplyPhysics(Entity aEntity, IEnumerable<Entity> aOthers)
		{
			if (aEntity.IsAbsorbed)
				return;

			var g = VectorExtensions.Zero;

			foreach (var other in aOthers.Where(e => !e.IsAbsorbed))
			{
				// Kollision behandeln
				var (v1, v2) = HandleCollision(aEntity, other, aEntity.World.ElasticCollisions);

				if (v1.HasValue && v2.HasValue)
				{
					var (position1, position2) = CancelOverlap(aEntity, other);

					if (position1.HasValue)
						mPositionByEntityId[aEntity.Id] = position1.Value;

					if (position2.HasValue)
						mPositionByEntityId[other.Id] = position2.Value;
				}

				if (v1.HasValue)
					mvByEntityId[aEntity.Id] = v1.Value;

				if (v2.HasValue)
					mvByEntityId[other.Id] = v2.Value;

				if (aEntity.IsAbsorbed)
					return;

				var dist = aEntity.Position - other.Position;

				// Gravitationsbeschleunigung integrieren
				g += other.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
			}

			mgByEntityId[aEntity.Id] = g;
		}

		private async Task ApplyPhysicsAsync(IReadOnlyCollection<Entity> aEntities)
		{
			mPositionByEntityId.Clear();
			mvByEntityId.Clear();
			mgByEntityId.Clear();

			await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										.Select(chunk => Task.Run(() =>
																  {
																	  foreach (var entity in chunk)
																		  ApplyPhysics(entity, aEntities.Except(entity));
																  })));
		}

		#endregion
	}
}