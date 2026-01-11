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
	private readonly IIntegrator _integrator;

	// Pool for per-partition collision collectors to reduce List<T> and array allocations
	private static readonly object _collectorPoolLock = new();
	private static readonly Stack<List<EntityTree.CollisionPair>> _collectorPool = new();
	private const int _maxCollectors = 1024;

	#endregion

	#region Construction

	public BarnesHutSimulationEngine()
		: this(new RungeKuttaIntegrator())
	{
	}

	public BarnesHutSimulationEngine(IIntegrator integrator)
	{
		_integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));
	}

	#endregion

	#region Implementation of ISimulationEngine

	/// <inheritdoc/>
	void ISimulationEngine.Simulate(Entity[] entities, TimeSpan deltaTime)
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

		// Pool world-boundary collision work: precompute viewport bounds once
		var vp = entities.Length > 0 ? entities[0].World.Viewport : null;
		if (vp != null)
		{
			var topLeft = vp.TopLeft;
			var bottomRight = vp.BottomRight;
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries(in topLeft, in bottomRight);
		}
		else
		{
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries();
		}
	}

	#endregion

	#region Implementation

	private static List<EntityTree.CollisionPair> RentCollector()
	{
		lock(_collectorPoolLock)
		{
			if(_collectorPool.Count > 0)
				return _collectorPool.Pop();
		}

		return new List<EntityTree.CollisionPair>(64);
	}

	private static void ReturnCollector(List<EntityTree.CollisionPair> list)
	{
		list.Clear();
		// Trim excessive capacity to keep array sizes bounded
		if(list.Capacity > 4096)
			list.Capacity = 4096;

		lock(_collectorPoolLock)
		{
			if(_collectorPool.Count < _maxCollectors)
				 _collectorPool.Push(list);
		}
	}

	// Synchronous physics application using fixed-chunk Parallel.For
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

		var n = entities.Length;
		// Choose a cache-friendly block size relative to CPU count and entity count
		var workers = Math.Max(1, Environment.ProcessorCount);
		var blockSize = Math.Max(256, n / (workers * 8)); // favor moderate chunks to reduce scheduling overhead
		if(blockSize < 256) blockSize = 256;

		// Range partitioning avoids per-iteration loop-state overhead from Parallel.For(int)
		var ranges = Partitioner.Create(0, n, blockSize);
		Parallel.ForEach(ranges, range =>
		{
			var (start, end) = range;
			var localCollisions = RentCollector();
			for (var i = start; i < end; i++)
				entities[i].a = tree.CalculateGravity(entities[i], localCollisions);

			if(localCollisions.Count > 0)
			{
				lock(tree.CollidedEntities)
					tree.CollidedEntities.AddRange(localCollisions);
			}
			ReturnCollector(localCollisions);
		});

		// Project collisions to id tuples
		var collisions = tree.CollidedEntities;
		var result = new Tuple<int, int>[collisions.Count];

		for(var i = 0; i < collisions.Count; i++)
		{
			var c = collisions[i];
			result[i] = Tuple.Create(c.First.Id, c.Second.Id);
		}

		// Return nodes to pool
		tree.Release();

		return result;
	}

	#endregion
}