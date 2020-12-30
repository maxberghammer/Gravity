using System;
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
		private readonly Dictionary<int, Vector> mvByEntityId = new Dictionary<int, Vector>();

		// ReSharper disable once InconsistentNaming
		private readonly Dictionary<int, Vector> mgByEntityId = new Dictionary<int, Vector>();

		#endregion

		#region Implementation of ISimulationEngine

		/// <inheritdoc />
		async Task ISimulationEngine.SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime)
		{
			// Objekte updaten
			foreach (var entity in aEntities)
				Update(entity,
					   mvByEntityId.TryGetValue(entity.Id, out var v)
						   ? v
						   : (Vector?)null,
					   mgByEntityId.TryGetValue(entity.Id, out var g)
						   ? g
						   : (Vector?)null,
					   aDeltaTime);

			// Berechnete Physikdaten zurücksetzen
			mvByEntityId.Clear();
			mgByEntityId.Clear();

			// Physik anwenden
			await ApplyPhysicsAsync(aEntities.Where(e => !e.IsAbsorbed)
											 .ToArray());
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
			=> await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										   .Select(chunk => Task.Run(() =>
																	 {
																		 foreach (var entity in chunk)
																			 ApplyPhysics(entity, aEntities.Except(entity));
																	 })));

		#endregion
	}
}