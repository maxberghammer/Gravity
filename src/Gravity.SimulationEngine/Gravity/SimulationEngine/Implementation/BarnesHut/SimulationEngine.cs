// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation.BarnesHut;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Fields

	private const int _maxCollectors = 1024;
	private static readonly Stack<List<BarnesHutTree.CollisionPair>> _collectorPool = new();

	// Pool for per-partition collision collectors to reduce List<T> and array allocations
	private static readonly object _collectorPoolLock = new();

	// Reuse a HashSet for collision de-dup to avoid per-frame allocations
	private readonly HashSet<long> _collisionKeys = new(1024);
	private readonly IIntegrator _integrator;

	#endregion

	#region Construction

	public SimulationEngine(IIntegrator integrator)
		=> _integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnSimulate(IWorld world, Body[] bodies, TimeSpan deltaTime)
	{
		// Physik anwenden und integrieren (synchron, aber parallelisiert)
		var collisions = _integrator.Integrate(bodies, deltaTime, ApplyPhysics);

		// Kollisionen behandeln
		if(collisions.Length != 0)
		{
			var bodiesById = bodies.ToDictionary(e => e.Id);

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

				var body1 = bodiesById[a];
				var body2 = bodiesById[b];

				(var v1, var v2) = HandleCollision(body1, body2, world.ElasticCollisions);

				if(v1.HasValue &&
				   v2.HasValue)
				{
					(var position1, var position2) = CancelOverlap(body1, body2);

					if(position1.HasValue)
						body1.Position = position1.Value;

					if(position2.HasValue)
						body2.Position = position2.Value;
				}

				if(v1.HasValue)
					body1.v = v1.Value;

				if(v2.HasValue)
					body2.v = v2.Value;
			}
		}
	}

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

	// Synchronous physics application using Parallel.For with thread-local collectors
	private static Tuple<int, int>[] ApplyPhysics(Body[] bodies)
	{
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < bodies.Length; i++)
		{
			var p = bodies[i].Position;
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
		var n = bodies.Length;
		var width = Math.Max(1e-12, r - l);
		var height = Math.Max(1e-12, b - t);
		var span = Math.Max(width, height);
		// Optional mild geometry factor
		var minSep = double.PositiveInfinity;

		for(var i = 0; i < Math.Min(n, 32); i++)
		{
			for(var j = i + 1; j < Math.Min(n, i + 32); j++)
			{
				var d = (bodies[j].Position - bodies[i].Position).Length;
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

		for(var i = 0; i < bodies.Length; i++)
			tree.Add(bodies[i]);

		tree.ComputeMassDistribution();

		// Parallel.For with thread-local collision collectors
		Parallel.For(0, n,
			localInit: () => RentCollector(),
			body: (i, state, localCollisions) =>
			{
				bodies[i].a = tree.CalculateGravity(bodies[i], localCollisions);
				return localCollisions;
			},
			localFinally: localCollisions =>
			{
				if(localCollisions.Count > 0)
				{
					lock(tree.CollidedBodies)
						tree.CollidedBodies.AddRange(localCollisions);
				}
				ReturnCollector(localCollisions);
			});

		var collisions = tree.CollidedBodies;
		var result = collisions.Count == 0 ? Array.Empty<Tuple<int, int>>() : new Tuple<int, int>[collisions.Count];

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