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

		// Bounds and radius stats - now including Z for 3D spatial hashing
		double minX = double.PositiveInfinity,
			   minY = double.PositiveInfinity,
			   minZ = double.PositiveInfinity,
			   maxX = double.NegativeInfinity,
			   maxY = double.NegativeInfinity,
			   maxZ = double.NegativeInfinity,
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
			if(p.X < minX)
				minX = p.X;
			if(p.Y < minY)
				minY = p.Y;
			if(p.Z < minZ)
				minZ = p.Z;
			if(p.X > maxX)
				maxX = p.X;
			if(p.Y > maxY)
				maxY = p.Y;
			if(p.Z > maxZ)
				maxZ = p.Z;
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

		if(double.IsInfinity(minX) ||
		   double.IsInfinity(minY) ||
		   double.IsInfinity(minZ) ||
		   double.IsInfinity(maxX) ||
		   double.IsInfinity(maxY) ||
		   double.IsInfinity(maxZ))
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

		var spanX = Math.Max(1e-12, maxX - minX);
		var spanY = Math.Max(1e-12, maxY - minY);
		var spanZ = Math.Max(1e-12, maxZ - minZ);
		var cols = Math.Max(1, (int)Math.Ceiling(spanX / cellSize) + 1);
		var rows = Math.Max(1, (int)Math.Ceiling(spanY / cellSize) + 1);
		var depths = Math.Max(1, (int)Math.Ceiling(spanZ / cellSize) + 1);

		// Allocate local buckets per call to avoid cross-thread interference (3D grid)
		var buckets = new List<int>[cols * rows * depths];

		static int Key3D(int x, int y, int z, int cols, int rows)
			=> z * cols * rows + y * cols + x;

		// Fill buckets from active bodies only
		foreach(var activeIndex in activeIndices)
		{
			var p = bodies[activeIndex].Position;
			var cx = (int)Math.Floor((p.X - minX) / cellSize);
			var cy = (int)Math.Floor((p.Y - minY) / cellSize);
			var cz = (int)Math.Floor((p.Z - minZ) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			cz = Math.Min(Math.Max(cz, 0), depths - 1);
			var k = Key3D(cx, cy, cz, cols, rows);
			var bucket = buckets[k];
			if(bucket == null)
				buckets[k] = bucket = new(4);
			bucket.Add(activeIndex);
		}

		var elastic = world.ElasticCollisions;

		// Neighbor scanning using active bodies; keep guards in case of mid-pass absorption.
		foreach(var i in activeIndices)
		{
			// body may have been absorbed by a previous merge
			if(bodies[i].IsAbsorbed)
				continue;

			var body1 = bodies[i];
			var p = body1.Position;
			var cx = (int)Math.Floor((p.X - minX) / cellSize);
			var cy = (int)Math.Floor((p.Y - minY) / cellSize);
			var cz = (int)Math.Floor((p.Z - minZ) / cellSize);
			cx = Math.Min(Math.Max(cx, 0), cols - 1);
			cy = Math.Min(Math.Max(cy, 0), rows - 1);
			cz = Math.Min(Math.Max(cz, 0), depths - 1);

			// per-entity neighbor range clamp based on size relative to cell
			int range;
			if(body1.r <= 0.5 * cellSize)
				range = 1;
			else if(body1.r <= 1.5 * cellSize)
				range = 2;
			else
				range = (int)Math.Ceiling(body1.r / cellSize) + 1;

			// 3D neighbor cell iteration
			for(var dz = -range; dz <= range; dz++)
			{
				var zz = cz + dz;
				if(zz < 0 || zz >= depths)
					continue;

				for(var dy = -range; dy <= range; dy++)
				{
					var yy = cy + dy;
					if(yy < 0 || yy >= rows)
						continue;

					for(var dx = -range; dx <= range; dx++)
					{
						var xx = cx + dx;
						if(xx < 0 || xx >= cols)
							continue;

						// Half-space de-dup: only process cells "ahead" in the 3D ordering
						// Compare (zz, yy, xx) > (cz, cy, cx) lexicographically
						if(zz < cz)
							continue;
						if(zz == cz && yy < cy)
							continue;
						if(zz == cz && yy == cy && xx < cx)
							continue;

						var bucket = buckets[Key3D(xx, yy, zz, cols, rows)];
						if(bucket == null)
							continue;

						foreach(var j in bucket)
						{
							// within same cell ensure j>i to avoid duplicates
							if(xx == cx && yy == cy && zz == cz && j <= i)
								continue;

							var body2 = bodies[j];

							if(body2.IsAbsorbed)
								continue;

							// Fast AABB rejection using radii
							var sumR = body1.r + body2.r;
							var diffX = body1.Position.X - body2.Position.X;

							if(Math.Abs(diffX) > sumR)
								continue;

							var diffY = body1.Position.Y - body2.Position.Y;

							if(Math.Abs(diffY) > sumR)
								continue;

							var diffZ = body1.Position.Z - body2.Position.Z;

							if(Math.Abs(diffZ) > sumR)
								continue;

							var d = new Vector3D(diffX, diffY, diffZ);
							var distSq = d.LengthSquared;

							if(distSq > sumR * sumR)
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
	}

	#endregion

	#region Implementation

	/// <summary>
	///     Behandelt die Überlappung zweier gegebener Objekte und liefert, falls eine Überlappung vorliegt, gegebenenfalls die
	///     neuen Positionen der beiden Objekte, so dass sie sich nicht mehr überlappen.
	/// </summary>
	private static (Vector3D? Position1, Vector3D? Position2) CancelOverlap(Body body1, Body body2)
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
	private static (Vector3D? v1, Vector3D? v2) HandleCollision(Body body1, Body body2, bool elastic)
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
