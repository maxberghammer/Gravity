// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class BarnesHutSimulationEngine : ISimulationEngine
{
	#region Fields

	private readonly IIntegrator _integrator = new NullIntegrator();

	#endregion

	#region Implementation of ISimulationEngine

	/// <inheritdoc/>
	async Task ISimulationEngine.SimulateAsync(Entity[] entities, TimeSpan deltaTime)
	{
		// Physik anwenden und integrieren
		var collisions = await _integrator.IntegrateAsync(entities, deltaTime, async es => await ApplyPhysicsAsync(es));

		// Kollisionen behandeln
		if(collisions.Length != 0)
		{
			var entitiesById = entities.ToDictionary(e => e.Id);

			foreach((var entity1, var entity2) in collisions.Select(t => Tuple.Create(entitiesById[Math.Min(t.Item1, t.Item2)],
																					  entitiesById[Math.Max(t.Item1, t.Item2)]))
															.Distinct())
			{
				(var v1, var v2) = entity1.HandleCollision(entity2, entity1.World.ElasticCollisions);

				if(v1.HasValue &&
				   v2.HasValue)
				{
					(var position1, var position2) = entity1.CancelOverlap(entity2);

					if(position1.HasValue)
						entity1.Position = position1.Value;

					if(position2.HasValue)
						entity2.Position = position2.Value;
				}

				if(v1.HasValue)
					entity1.v = v1.Value;

				if(v2.HasValue)
					entity2.v = v2.Value;
			}
		}

		foreach(var entity in entities)
			if(entity.World.ClosedBoundaries)
				entity.HandleCollisionWithWorldBoundaries();
	}

	#endregion

	#region Implementation

	private static async Task<Tuple<int, int>[]> ApplyPhysicsAsync(IReadOnlyCollection<Entity> entities)
	{
		var l = 0.0d;
		var t = 0.0d;
		var r = 0.0d;
		var b = 0.0d;

		foreach(var pos in entities.Select(e=>e.Position))
		{
			l = Math.Min(l, pos.X);
			t = Math.Min(t, pos.Y);
			r = Math.Max(r, pos.X);
			b = Math.Max(b, pos.Y);
		}

		var tree = new EntityTree(new(l, t), new(r, b), 1.0d);

		foreach(var entity in entities)
			tree.Add(entity);

		await Task.Run(() => tree.ComputeMassDistribution());

		// Gravitation berechnen
		await Task.WhenAll(entities.Chunked(IWorld.GetPreferredChunkSize(entities))
								   .Select(chunk => Task.Run(() =>
															 {
																 foreach(var entity in chunk)
																	 entity.a = tree.CalculateGravity(entity);
															 })));

		return tree.CollidedEntities
				   .Select(c => Tuple.Create(c.Item1.Id, c.Item2.Id))
				   .ToArray();
	}

	#endregion
}