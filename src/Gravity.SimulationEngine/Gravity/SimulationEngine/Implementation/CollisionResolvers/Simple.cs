using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.CollisionResolvers;

internal sealed class Simple : SimulationEngine.ICollisionResolver
{
	#region Fields

	private const double _cellScale = 2.0; // clamp median to avoid extremes; pick scale ~ average diameter

	#endregion

	#region Implementation of ICollisionResolver

	/// <inheritdoc/>
	void SimulationEngine.ICollisionResolver.ResolveCollisions(IWorld world, Body[] bodies, Diagnostics diagnostics)
	{
		var n = bodies.Length;

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

		// Build dense list of active indices while collecting stats
		var activeIndices = new List<int>(Math.Min(n, 4096));

		for(var i = 0; i < n; i++)
		{
			if(bodies[i].IsAbsorbed)
				continue;

			activeIndices.Add(i);
			active++;
			var p = bodies[i].Position;
			if(p.X < l)
				l = p.X;
			if(p.Y < t)
				t = p.Y;
			if(p.X > r)
				r = p.X;
			if(p.Y > b)
				b = p.Y;
			var ri = bodies[i].r;
			if(ri > rMax)
				rMax = ri;
			rSum += ri;
			// lightweight sampling for median
			if(sampleCount < rSample.Length &&
			   i % stride == 0)
				rSample[sampleCount++] = ri;
		}

		// Nothing or a single active body -> no collisions
		if(active <= 1)
			return;

		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
			return;

		// Adaptive cell size heuristic
		var rAvg = rSum / active;

		if(sampleCount == 0)
			rSample = [];
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

		// Allocate local buckets per call to avoid cross-thread interference
		var buckets = new List<int>[cols * rows];

		static int Key(int x, int y, int cols)
			=> y * cols + x;

		// Fill buckets from active bodies only
		foreach(var activeIndex in activeIndices)
		{
			var p = bodies[activeIndex].Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			var k = Key(cx, cy, cols);
			var bucket = buckets[k];
			if(bucket == null)
				buckets[k] = bucket = new(4);
			bucket.Add(activeIndex);
		}

		var elastic = world.ElasticCollisions;

		// Neighbor scanning using active bodies; keep guards in case of mid-pass absorption.
		// De-duplicate pairs by visiting only a forward half-plane of neighbor cells.
		foreach(var i in activeIndices)
		{
			// body may have been absorbed by a previous merge
			if(bodies[i].IsAbsorbed)
				continue;

			var body1 = bodies[i];
			var p = body1.Position;
			var cx = (int)Math.Floor((p.X - l) / cellSize);
			var cy = (int)Math.Floor((p.Y - t) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			// per-entity neighbor range clamp based on size relative to cell
			int range;
			if(body1.r <= 0.5 * cellSize)
				range = 1;
			else if(body1.r <= 1.5 * cellSize)
				range = 2;
			else
				range = (int)Math.Ceiling(body1.r / cellSize) + 1;

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

					// half-plane de-dup: skip buckets strictly above current row
					if(yy < cy)
						continue;

					// in the same row, skip buckets left of current cell
					if(yy == cy &&
					   xx < cx)
						continue;

					var bucket = buckets[Key(xx, yy, cols)];

					if(bucket == null)
						continue;

					foreach(var j in bucket)
					{
						// within same cell ensure j>i to avoid duplicates
						if(xx == cx &&
						   yy == cy &&
						   j <= i)
							continue;

						var body2 = bodies[j];

						if(body2.IsAbsorbed)
							continue;

						// Fast AABB rejection using radii
						var sumR = body1.r + body2.r;
						var dx = body1.Position.X - body2.Position.X;

						if(Math.Abs(dx) > sumR)
							continue;

						var dy = body1.Position.Y - body2.Position.Y;

						if(Math.Abs(dy) > sumR)
							continue;

						var d = new Vector2D(dx, dy);

						if(d.LengthSquared > sumR * sumR)
							continue;

						(var v1, var v2) = HandleCollision(body1, body2, elastic);

						if(v1.HasValue &&
						   v2.HasValue)
						{
							(var p1, var p2) = CancelOverlap(body1, body2);
							if(p1.HasValue)
								body1.Position = p1.Value;
							if(p2.HasValue)
								body2.Position = p2.Value;
						}

						if(v1.HasValue)
							body1.v = v1.Value;
						if(v2.HasValue)
							body2.v = v2.Value;
					}
				}
			}
		}
	}

	#endregion

	#region Implementation

	/// <summary>
	///     Behandelt die Überlappung zweier gegebener Objekte und liefert, falls eine Überlappung vorliegt, gegebenenfalls die
	///     neuen Positionen der beiden Objekte, so dass sie sich nicht mehr überlappen.
	/// </summary>
	private static (Vector2D? Position1, Vector2D? Position2) CancelOverlap(Body body1, Body body2)
	{
		ArgumentNullException.ThrowIfNull(body1);
		ArgumentNullException.ThrowIfNull(body2);

		var dist = body1.Position - body2.Position;
		var minDistAbs = body1.r + body2.r;
		var distUnit = dist.Unit(); // Compute unit vector only once

		if(body1.m < body2.m)
			return (body2.Position + distUnit * minDistAbs, null);

		if(body1.m > body2.m)
			return (null, body1.Position - distUnit * minDistAbs);

		return (body2.Position + (dist + distUnit * minDistAbs) / 2, body1.Position - (dist + distUnit * minDistAbs) / 2);
	}

	/// <summary>
	///     Behandelt die Kollision zweier gegebener Objekte und liefert, falls eine Kollision vorliegt, gegebenenfalls die
	///     neuen Geschwindigkeiten der beiden Objekte.
	/// </summary>
	private static (Vector2D? v1, Vector2D? v2) HandleCollision(Body body1, Body body2, bool elastic)
	{
		ArgumentNullException.ThrowIfNull(body1);
		ArgumentNullException.ThrowIfNull(body2);

		if(body1.IsAbsorbed ||
		   body2.IsAbsorbed)
			return (null, null);

		var dist = body1.Position - body2.Position;

		if(dist.Length >= body1.r + body2.r)
			return (null, null);

		if(elastic)
		{
			var temp = dist / (dist * dist);

			// Masse Objekt 1
			var m1 = body1.m;
			// Masse Objekt 2
			var m2 = body2.m;
			// Geschwindigkeitsvektor Objekt 1
			var v1 = body1.v;
			// Geschwindigkeitsvektor Objekt 2
			var v2 = body2.v;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1
			var vn1 = temp * (dist * v1);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2
			var vn2 = temp * (dist * v2);
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 1
			var vt1 = v1 - vn1;
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 2
			var vt2 = v2 - vn2;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1 so korrigieren, dass die Objekt-Massen mit einfließen
			var un1 = (m1 * vn1 + m2 * (2 * vn2 - vn1)) / (m1 + m2);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2 so korrigieren, dass die Objekt-Massen mit einfließen
			var un2 = (m2 * vn2 + m1 * (2 * vn1 - vn2)) / (m1 + m2);

			v1 = un1 + vt1;
			v2 = un2 + vt2;

			return (v1, v2);
		}

		// Vollständig unelastischer Stoß (Vereinigung): Impulserhaltung
		var mSum = body1.m + body2.m;
		var vMerged = (body1.m * body1.v + body2.m * body2.v) / mSum;

		if(body2.m > body1.m)
		{
			body2.Absorb(body1);

			return (null, vMerged);
		}

		body1.Absorb(body2);

		return (vMerged, null);
	}

	#endregion
}