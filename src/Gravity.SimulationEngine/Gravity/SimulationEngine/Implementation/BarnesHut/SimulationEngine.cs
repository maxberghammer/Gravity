// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation.BarnesHut;

internal sealed class SimulationEngine : ISimulationEngine
{
	#region Fields

	private const int _maxCollectors = 1024;
	private static readonly Stack<List<BarnesHutTree.CollisionPair>> _collectorPool = new();

	// Pool for per-partition collision collectors to reduce List<T> and array allocations
	private static readonly object _collectorPoolLock = new();

	// Reuse a HashSet for collision de-dup to avoid per-frame allocations
	private readonly HashSet<long> _collisionKeys = new(1024);
	private readonly Diagnostics _diagnostics = new();
	private readonly IIntegrator _integrator;

	#endregion

	#region Construction

	public SimulationEngine(IIntegrator integrator)
		=> _integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));

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
		var vp = entities.Length > 0
					 ? entities[0].World.Viewport
					 : null;

		if(vp != null)
		{
			var topLeft = vp.TopLeft;
			var bottomRight = vp.BottomRight;
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries(in topLeft, in bottomRight);
		}
		else
			for(var i = 0; i < entities.Length; i++)
				if(entities[i].World.ClosedBoundaries)
					entities[i].HandleCollisionWithWorldBoundaries();
	}

	ISimulationEngine.IDiagnostics ISimulationEngine.GetDiagnostics()
		=> _diagnostics;

	#endregion

	#region Implementation

	private static List<BarnesHutTree.CollisionPair> RentCollector()
	{
		lock(_collectorPoolLock)
			if(_collectorPool.Count > 0)
				return _collectorPool.Pop();

		return new(64);
	}

	private static void ReturnCollector(List<BarnesHutTree.CollisionPair> list)
	{
		list.Clear();
		// Trim excessive capacity to keep array sizes bounded
		if(list.Capacity > 4096)
			list.Capacity = 4096;

		lock(_collectorPoolLock)
			if(_collectorPool.Count < _maxCollectors)
				_collectorPool.Push(list);
	}

	// Synchronous physics application using fixed-chunk Parallel.For
	private static Tuple<int, int>[] ApplyPhysics(Entity[] entities)
	{
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < entities.Length; i++)
		{
			var p = entities[i].Position;
			if(p.X < l)
				l = p.X;
			if(p.Y < t)
				t = p.Y;
			if(p.X > r)
				r = p.X;
			if(p.Y > b)
				b = p.Y;
		}

		// Fallback to a minimal box around origin if no entities
		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
		{
			l = t = -1.0;
			r = b = 1.0;
		}

		// n-based theta schedule targeting near-constant per-body work, with small-N overrides for accuracy
		var n = entities.Length;
		var width = Math.Max(1e-12, r - l);
		var height = Math.Max(1e-12, b - t);
		var span = Math.Max(width, height);
		// Optional mild geometry factor
		var minSep = double.PositiveInfinity;

		for(var i = 0; i < Math.Min(n, 32); i++)
		{
			for(var j = i + 1; j < Math.Min(n, i + 32); j++)
			{
				var d = (entities[j].Position - entities[i].Position).Length;
				if(d < minSep)
					minSep = d;
			}
		}

		if(double.IsInfinity(minSep) ||
		   minSep <= 0)
			minSep = span;
		var sepRatio = Math.Clamp(minSep / span, 0.0, 1.0);

		double theta;

		// Small-N exactness: effectively disable aggregation
		if(n <= 3)
			theta = 0.0; // pairwise exact
		else if(n <= 10)
			theta = 0.1;
		else if(n <= 50)
			theta = 0.2;
		else
		{
			// Base schedule: theta(n) = base + k*log10(n), clamped
			var baseTheta = 0.62;
			var k = 0.22;
			var raw = baseTheta + k * Math.Log10(Math.Max(1, n));
			// Apply mild geometry factor
			raw *= 0.9 + 0.2 * sepRatio;
			theta = Math.Clamp(raw, 0.6, 1.2);
		}

		var tree = new BarnesHutTree(new(l, t), new(r, b), theta);

		for(var i = 0; i < entities.Length; i++)
			tree.Add(entities[i]);

		tree.ComputeMassDistribution();

		// Choose a cache-friendly block size relative to CPU count and entity count
		var workers = Math.Max(1, Environment.ProcessorCount);
		var blockSize = Math.Max(256, n / (workers * 8));

		var ranges = Partitioner.Create(0, n, blockSize);
		Parallel.ForEach(ranges, range =>
								 {
									 (var start, var end) = range;
									 var localCollisions = RentCollector();
									 for(var i = start; i < end; i++)
										 entities[i].a = tree.CalculateGravity(entities[i], localCollisions);

									 if(localCollisions.Count > 0)
										 lock(tree.CollidedEntities)
											 tree.CollidedEntities.AddRange(localCollisions);
									 ReturnCollector(localCollisions);
								 });

		var collisions = tree.CollidedEntities;
		var result = new Tuple<int, int>[collisions.Count];

		for(var i = 0; i < collisions.Count; i++)
		{
			var c = collisions[i];
			result[i] = Tuple.Create(c.First.Id, c.Second.Id);
		}

		tree.Release();

		return result;
	}

	#endregion
}