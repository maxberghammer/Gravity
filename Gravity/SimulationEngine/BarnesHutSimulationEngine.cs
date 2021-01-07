using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Gravity.SimulationEngine.Integrators;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class BarnesHutSimulationEngine : SimulationEngine, ISimulationEngine
	{
		#region Fields

		private readonly IIntegrator mIntegrator = new NullIntegrator();

		#endregion

		#region Implementation of ISimulationEngine

		/// <inheritdoc />
		async Task ISimulationEngine.SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime)
		{
			// Physik anwenden und integrieren
			var collisions = await mIntegrator.IntegrateAsync(aEntities, aDeltaTime, async entities => await ApplyPhysicsAsync(entities));

			// Kollisionen behandeln
			if (collisions.Any())
			{
				var entitiesById = aEntities.ToDictionary(e => e.Id);

				foreach (var (entity1, entity2) in collisions.Select(t => Tuple.Create(entitiesById[Math.Min(t.Item1, t.Item2)],
																					   entitiesById[Math.Max(t.Item1, t.Item2)]))
															 .Distinct())
				{
					var (v1, v2) = HandleCollision(entity1, entity2, entity1.World.ElasticCollisions);

					if (v1.HasValue && v2.HasValue)
					{
						var (position1, position2) = CancelOverlap(entity1, entity2);

						if (position1.HasValue)
							entity1.Position = position1.Value;

						if (position2.HasValue)
							entity2.Position = position2.Value;
					}

					if (v1.HasValue)
						entity1.v = v1.Value;

					if (v2.HasValue)
						entity2.v = v2.Value;
				}
			}

			foreach (var entity in aEntities)
				if (entity.World.ClosedBoundaries)
					HandleCollisionWithWorldBoundaries(entity);
		}

		#endregion

		#region Implementation

		private async Task<Tuple<int, int>[]> ApplyPhysicsAsync(IReadOnlyCollection<Entity> aEntities)
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

			// Gravitation berechnen
			await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										.Select(chunk => Task.Run(() =>
																  {
																	  foreach (var entity in chunk)
																		  entity.a = tree.CalculateGravity(entity);
																  })));


			return tree.CollidedEntities
					   .Select(c => Tuple.Create(c.Item1.Id, c.Item2.Id))
					   .ToArray();
		}

		#endregion
	}
}