// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class BarnesHutSimulationEngine : ISimulationEngine
{
	#region Fields

	// Reuse a HashSet for collision de-dup to avoid per-frame allocations
	private readonly HashSet<long> _collisionKeys = new(1024);
	private readonly IIntegrator _integrator = new NullIntegrator();

	#endregion

	#region Implementation of ISimulationEngine

	/// <inheritdoc/>
	Task ISimulationEngine.SimulateAsync(Entity[] entities, TimeSpan deltaTime)
	{
		// Physik anwenden und integrieren (synchron, aber parallelisiert)
		var collisions = _integrator.Integrate(entities, deltaTime, ApplyPhysics);

		// Kollisionen behandeln
		if(collisions.Length != 0)
		{
			var entitiesById = entities.ToDictionary(e => e.Id);

			// De-dup collisions using a pooled HashSet<long> with normalized pair key (minId<<32 | maxId)
			_collisionKeys.Clear();

			for(var i = 0; i < collisions.Length; i++)
			{
				(var id1, var id2) = collisions[i];
				var a = Math.Min(id1, id2);
				var b = Math.Max(id1, id2);
				var key = ((long)a << 32) | (uint)b;

				if(!_collisionKeys.Add(key))
					continue;

				var entity1 = entitiesById[a];
				var entity2 = entitiesById[b];

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

		for(var i = 0; i < entities.Length; i++)
			if(entities[i].World.ClosedBoundaries)
				entities[i].HandleCollisionWithWorldBoundaries();

		return Task.CompletedTask;
	}

	#endregion

	#region Implementation

	// Synchronous physics application using Parallel.ForEach over range partitions
	private static Tuple<int, int>[] ApplyPhysics(Entity[] entities)
	{
		double l = 0.0d,
			   t = 0.0d,
			   r = 0.0d,
			   b = 0.0d;

		for(var i = 0; i < entities.Length; i++)
		{
			var pos = entities[i].Position;
			l = Math.Min(l, pos.X);
			t = Math.Min(t, pos.Y);
			r = Math.Max(r, pos.X);
			b = Math.Max(b, pos.Y);
		}

		var tree = new EntityTree(new(l, t), new(r, b), 1.0d);

		for(var i = 0; i < entities.Length; i++)
			tree.Add(entities[i]);

		tree.ComputeMassDistribution();

		var partitions = Partitioner.Create(0, entities.Length);
		Parallel.ForEach(partitions, range =>
									 {
										 // per-partition collision collector
										 var localCollisions = new List<EntityTree.CollisionPair>(64);
										 for(var i = range.Item1; i < range.Item2; i++)
											 entities[i].a = tree.CalculateGravity(entities[i], localCollisions);

										 // merge once for this partition
										 if(localCollisions.Count > 0)
											 lock(tree.CollidedEntities)
												 tree.CollidedEntities.AddRange(localCollisions);
									 });

		// Project collisions to id tuples
		var collisions = tree.CollidedEntities;
		var result = new Tuple<int, int>[collisions.Count];

		for(var i = 0; i < collisions.Count; i++)
		{
			var c = collisions[i];
			result[i] = Tuple.Create(c.First.Id, c.Second.Id);
		}

		tree.ResetCollisions();

		return result;
	}

	#endregion
}