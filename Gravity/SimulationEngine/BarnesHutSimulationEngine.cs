using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class BarnesHutSimulationEngine : SimulationEngine, ISimulationEngine
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
		public async Task SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime)
		{
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

			// Physik anwenden
			await ApplyPhysicsAsync(aEntities.Where(e => !e.IsAbsorbed)
											 .ToArray());
		}

		#endregion

		#region Implementation
		
		private async Task ApplyPhysicsAsync(IReadOnlyCollection<Entity> aEntities)
		{
			var l = 0.0d;
			var t = 0.0d;
			var r = 0.0d;
			var b = 0.0d;

			foreach (var entity in aEntities)
			{
				l = Math.Min(l, entity.Position.X);
				t = Math.Min(t, entity.Position.Y);
				r = Math.Max(r, entity.Position.X);
				b = Math.Max(b, entity.Position.Y);
			}

			var tree = new EntityTree(new Vector(l, t), new Vector(r, b), 1.0d);

			foreach (var entity in aEntities)
				tree.Add(entity);

			await Task.Run(() => tree.ComputeMassDistribution());

			mPositionByEntityId.Clear();
			mvByEntityId.Clear();
			mgByEntityId.Clear();

			// Gravitation berechnen
			await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										.Select(chunk => Task.Run(() =>
																  {
																	  foreach (var entity in chunk)
																		  mgByEntityId[entity.Id] = tree.CalculateGravity(entity);
																  })));

			// Kollisionen behandeln
			foreach (var (entity1, entity2) in tree.CollidedEntities)
			{
				var (v1, v2) = HandleCollision(entity1, entity2, entity1.World.ElasticCollisions);

				if (v1.HasValue && v2.HasValue)
				{
					var (position1, position2) = CancelOverlap(entity1, entity2);

					if (position1.HasValue)
						mPositionByEntityId[entity1.Id] = position1.Value;

					if (position2.HasValue)
						mPositionByEntityId[entity2.Id] = position2.Value;
				}

				if (v1.HasValue)
					mvByEntityId[entity1.Id] = v1.Value;

				if (v2.HasValue)
					mvByEntityId[entity2.Id] = v2.Value;
			}

		}


		#endregion
	}
}