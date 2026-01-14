using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Adaptive.Integrators;
using Gravity.SimulationEngine.Implementation.Oversamplers;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Fields

	private const double _cellScale = 2.0; // clamp median to avoid extremes; pick scale ~ average diameter
	private static readonly object _sBucketsLock = new();

	// Reuse HashSet for collision de-dup
	private static readonly HashSet<long> _sSeen = new(1024);

	// Pooled grid buckets to avoid per-substep allocations
	private static List<int>[]? _sBuckets;
	private readonly IIntegrator _integrator;
	private readonly IOversampler _oversampler;

	#endregion

	#region Construction

	public SimulationEngine(IIntegrator integrator, IOversampler oversampler)
	{
		_integrator = integrator;
		_oversampler = oversampler;
	}

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnSimulate(IWorld world, Body[] entities, TimeSpan deltaTime)
	{
		var steps = _oversampler.Oversample(entities, deltaTime, (e, dt) =>
																 {
																	 // Integrator-Step (berechnet a intern wo nötig)
																	 _integrator.Step(e, dt.TotalSeconds, ComputeAccelerations);

																	 // Kollisionen nach dem Step exakt erkennen und auflösen (Oversampling)
																	 ResolveCollisions(world, e);
																 });

		// Report substeps for diagnostics
		Diagnostics.SetField("Substeps", steps);
	}

	private static void EnsureBuckets(int cols, int rows)
	{
		var size = cols * rows;

		lock(_sBucketsLock)
			if(_sBuckets == null ||
			   _sBuckets.Length != size)
				_sBuckets = new List<int>[size];
			else
				// Clear existing lists for reuse
				for(var i = 0; i < _sBuckets.Length; i++)
				{
					var list = _sBuckets[i];
					if(list != null)
						list.Clear();
				}
	}

	private static void ResolveCollisions(IWorld world, Body[] entities)
	{
		var n = entities.Length;

		if(n <= 1)
			return;

		// Bounds and radius stats
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity,
			   rMax = 0.0,
			   rSum = 0.0;
		var active = 0;
		var rSample = new double[Math.Min(128, Math.Max(n, 1))];
		var sampleCount = 0;
		var stride = Math.Max(1, n / Math.Max(1, rSample.Length));

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			active++;
			var p = entities[i].Position;
			if(p.X < l)
				l = p.X;
			if(p.Y < t)
				t = p.Y;
			if(p.X > r)
				r = p.X;
			if(p.Y > b)
				b = p.Y;
			var ri = entities[i].r;
			if(ri > rMax)
				rMax = ri;
			rSum += ri;
			// lightweight sampling for median
			if(sampleCount < rSample.Length &&
			   i % stride == 0)
				rSample[sampleCount++] = ri;
		}

		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
			return;

		// Adaptive cell size heuristic
		var rAvg = active > 0
					   ? rSum / active
					   : 0.0;

		if(sampleCount == 0)
			rSample = Array.Empty<double>();
		else if(sampleCount < rSample.Length)
		{
			// shrink array view
			var tmp = new double[sampleCount];
			Array.Copy(rSample, tmp, sampleCount);
			rSample = tmp;
		}

		if(rSample.Length > 0)
			Array.Sort(rSample);
		var rMed = rSample.Length > 0
					   ? rSample[rSample.Length / 2]
					   : rAvg > 0
						   ? rAvg
						   : rMax;

		var baseR = Math.Min(rMax, Math.Max(rMed, 0.25 * rMax));
		var cellSize = Math.Max(1e-9, _cellScale * baseR);

		var spanX = Math.Max(1e-12, r - l);
		var spanY = Math.Max(1e-12, b - t);
		var cols = Math.Max(1, (int)Math.Ceiling(spanX / cellSize) + 1);
		var rows = Math.Max(1, (int)Math.Ceiling(spanY / cellSize) + 1);

		EnsureBuckets(cols, rows);
		var buckets = _sBuckets!; // ensured non-null

		static int Key(int x, int y, int cols)
			=> y * cols + x;

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			var p = entities[i].Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var k = Key(cx, cy, cols);
			var bucket = buckets[k];
			if(bucket == null)
				buckets[k] = bucket = new(4);
			bucket.Add(i);
		}

		var elastic = world.ElasticCollisions;
		_sSeen.Clear();

		for(var i = 0; i < n; i++)
		{
			if(entities[i].IsAbsorbed)
				continue;

			var ei = entities[i];
			var p = ei.Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			// per-entity neighbor range clamp based on size relative to cell
			int range;
			if(ei.r <= 0.5 * cellSize)
				range = 1;
			else if(ei.r <= 1.5 * cellSize)
				range = 2;
			else
				range = (int)Math.Ceiling(ei.r / cellSize) + 1;

			for(var dv = -range; dv <= range; dv++)
			{
				var yy = cy + dv;

				if(yy < 0 ||
				   yy >= rows)
					continue;

				for(var du = -range; du <= range; du++)
				{
					var xx = cx + du;

					if(xx < 0 ||
					   xx >= cols)
						continue;

					var bucket = buckets[Key(xx, yy, cols)];

					if(bucket == null)
						continue;

					for(var bi = 0; bi < bucket.Count; bi++)
					{
						var j = bucket[bi];

						if(j <= i)
							continue;

						var ej = entities[j];

						if(ej.IsAbsorbed)
							continue;

						var a = Math.Min(ei.Id, ej.Id);
						var bId = Math.Max(ei.Id, ej.Id);
						var key = ((long)a << 32) | (uint)bId;

						if(!_sSeen.Add(key))
							continue;

						// Fast AABB rejection using radii
						var sumR = ei.r + ej.r;
						var dx = ei.Position.X - ej.Position.X;

						if(Math.Abs(dx) > sumR)
							continue;

						var dy = ei.Position.Y - ej.Position.Y;

						if(Math.Abs(dy) > sumR)
							continue;

						var d = new Vector2D(dx, dy);

						if(d.LengthSquared <= sumR * sumR)
						{
							(var v1, var v2) = HandleCollision(ei, ej, elastic);

							if(v1.HasValue &&
							   v2.HasValue)
							{
								(var p1, var p2) = CancelOverlap(ei, ej);
								if(p1.HasValue)
									ei.Position = p1.Value;
								if(p2.HasValue)
									ej.Position = p2.Value;
							}

							if(v1.HasValue)
								ei.v = v1.Value;
							if(v2.HasValue)
								ej.v = v2.Value;
						}
					}
				}
			}
		}
	}

	private static double ComputeTheta(Body[] entities, double l, double t, double r, double b)
	{
		var n = entities.Length;
		var width = Math.Max(1e-12, r - l);
		var height = Math.Max(1e-12, b - t);
		var span = Math.Max(width, height);
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

		if(n <= 3)
			return 0.0;
		if(n <= 10)
			return 0.1;
		if(n <= 50)
			return 0.2;

		var baseTheta = 0.62;
		var k = 0.22;
		var raw = baseTheta + k * Math.Log10(Math.Max(1, n));
		raw *= 0.9 + 0.2 * sepRatio;

		return Math.Clamp(raw, 0.6, 1.0);
	}

	private void ComputeAccelerations(Body[] entities)
	{
		var n = entities.Length;

		if(n == 0)
			return;

		// Bounds bestimmen
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < n; i++)
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

		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
		{
			l = t = -1.0;
			r = b = 1.0;
		}

		// Theta adaptiv wie in Barnes–Hut (inkl. Small-N-Overrides)
		var theta = ComputeTheta(entities, l, t, r, b);

		var tree = new BarnesHutTree(new(l, t), new(r, b), theta, n);
		// Presort by Morton-order for better locality
		tree.AddRange(entities);
		tree.ComputeMassDistribution();
		tree.CollectDiagnostics = false;
		// Update diagnostics locally
		Parallel.For(0, n, i => { entities[i].a = tree.CalculateGravity(entities[i]); });
		Diagnostics.SetField("Nodes", tree.NodeCount);
		Diagnostics.SetField("MaxDepth", tree.MaxDepthReached);
		Diagnostics.SetField("Visits", tree.TraversalVisitCount);

		tree.Release();
	}

	#endregion
}