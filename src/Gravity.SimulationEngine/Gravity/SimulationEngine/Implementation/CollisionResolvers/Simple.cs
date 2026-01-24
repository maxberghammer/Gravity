using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Gravity.SimulationEngine.Implementation.CollisionResolvers;

internal sealed class Simple : SimulationEngine.ICollisionResolver
{
	#region Fields

	private const double _cellScale = 2.0; // clamp median to avoid extremes; pick scale ~ average diameter

	// Persistent bucket structure - reused across frames to avoid allocations
	private List<int>?[] _buckets = [];
	private int _bucketCapacity;
	
	// Track which buckets were used for efficient clearing
	private int[] _usedBucketIndices = [];
	private int _usedBucketCount;
	
	// Pool of reusable List<int> objects
	private readonly Stack<List<int>> _listPool = new(64);

	#endregion

	#region Implementation of ICollisionResolver

	/// <inheritdoc/>
	void SimulationEngine.ICollisionResolver.ResolveCollisions(IWorld world, IReadOnlyList<Body> bodies, Diagnostics diagnostics)
	{
		var n = bodies.Count;

		if(n <= 1)
			return;

		// Use pooled array for active indices to avoid heap allocations
		var activePool = ArrayPool<int>.Shared;
		var activeIndices = activePool.Rent(n);
		var activeCount = 0;

		// Bounds and radius stats - now including Z for 3D spatial hashing
		double minX = double.PositiveInfinity,
			   minY = double.PositiveInfinity,
			   minZ = double.PositiveInfinity,
			   maxX = double.NegativeInfinity,
			   maxY = double.NegativeInfinity,
			   maxZ = double.NegativeInfinity,
			   rMax = 0.0,
			   rSum = 0.0;

		// Use stackalloc for small sample array (128 doubles = 1KB)
		Span<double> rSample = stackalloc double[128];
		var sampleCount = 0;
		var sampleCapacity = Math.Min(128, n);
		var stride = Math.Max(1, n / Math.Max(1, sampleCapacity));

		for(var i = 0; i < n; i++)
		{
			if(bodies[i].IsAbsorbed)
				continue;

			activeIndices[activeCount++] = i;
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
			if(sampleCount < sampleCapacity && i % stride == 0)
				rSample[sampleCount++] = ri;
		}

		// Nothing or a single active body -> no collisions
		if(activeCount <= 1)
		{
			activePool.Return(activeIndices);
			return;
		}

		if(double.IsInfinity(minX) ||
		   double.IsInfinity(minY) ||
		   double.IsInfinity(minZ) ||
		   double.IsInfinity(maxX) ||
		   double.IsInfinity(maxY) ||
		   double.IsInfinity(maxZ))
		{
			activePool.Return(activeIndices);
			return;
		}

		// Adaptive cell size heuristic
		var rAvg = rSum / activeCount;
		var rSampleSlice = rSample.Slice(0, sampleCount);
		rSampleSlice.Sort();
		var rMed = sampleCount > 0
					   ? rSampleSlice[sampleCount / 2]
					   : rAvg > 0
						   ? rAvg
						   : rMax;

		var baseR = Math.Min(rMax, Math.Max(rMed, 0.25 * rMax));
		var cellSize = Math.Max(1e-9, _cellScale * baseR);
		var invCellSize = 1.0 / cellSize; // Precompute inverse for faster division

		var spanX = Math.Max(1e-12, maxX - minX);
		var spanY = Math.Max(1e-12, maxY - minY);
		var spanZ = Math.Max(1e-12, maxZ - minZ);
		var cols = Math.Max(1, (int)Math.Ceiling(spanX * invCellSize) + 1);
		var rows = Math.Max(1, (int)Math.Ceiling(spanY * invCellSize) + 1);
		var depths = Math.Max(1, (int)Math.Ceiling(spanZ * invCellSize) + 1);

		// Precompute colsRows for faster key calculation
		var colsRows = cols * rows;
		var gridSize = colsRows * depths;

		// Clear buckets from previous frame and return lists to pool
		// IMPORTANT: Always null out slots to prevent stale data when grid dimensions change
		for(var i = 0; i < _usedBucketCount; i++)
		{
			var bucketIdx = _usedBucketIndices[i];
			var oldBucket = _buckets[bucketIdx];
			if(oldBucket != null)
			{
				oldBucket.Clear();
				_listPool.Push(oldBucket);
				_buckets[bucketIdx] = null;
			}
		}
		_usedBucketCount = 0;

		// Ensure persistent bucket array is large enough
		if(gridSize > _bucketCapacity)
		{
			// Grow arrays (use power of 2 for better memory alignment)
			var newCapacity = Math.Max(gridSize, _bucketCapacity * 2);
			newCapacity = (int)Math.Pow(2, Math.Ceiling(Math.Log2(newCapacity)));
			_buckets = new List<int>?[newCapacity];
			_usedBucketIndices = new int[Math.Max(activeCount * 2, 256)];
			_bucketCapacity = newCapacity;
		}
		else
		{
			// Ensure used indices array is large enough
			if(_usedBucketIndices.Length < activeCount)
				_usedBucketIndices = new int[activeCount * 2];
		}

		var buckets = _buckets;
		var usedBucketIndices = _usedBucketIndices;
		var usedBucketCount = 0;

		// Fill buckets from active bodies only
		for(var idx = 0; idx < activeCount; idx++)
		{
			var activeIndex = activeIndices[idx];
			var p = bodies[activeIndex].Position;
			var cx = Math.Clamp((int)((p.X - minX) * invCellSize), 0, cols - 1);
			var cy = Math.Clamp((int)((p.Y - minY) * invCellSize), 0, rows - 1);
			var cz = Math.Clamp((int)((p.Z - minZ) * invCellSize), 0, depths - 1);
			var k = cz * colsRows + cy * cols + cx;
			var bucket = buckets[k];
			if(bucket == null)
			{
				// Get a list from the pool or create a new one
				bucket = _listPool.Count > 0 ? _listPool.Pop() : new List<int>(8);
				buckets[k] = bucket;
				usedBucketIndices[usedBucketCount++] = k;
			}
			bucket.Add(activeIndex);
		}
		
		_usedBucketCount = usedBucketCount;

		var elastic = world.ElasticCollisions;

		// Neighbor scanning using active bodies; keep guards in case of mid-pass absorption.
		for(var idx = 0; idx < activeCount; idx++)
		{
			var i = activeIndices[idx];

			// body may have been absorbed by a previous merge
			if(bodies[i].IsAbsorbed)
				continue;

			var body1 = bodies[i];
			var p = body1.Position;
			var cx = Math.Clamp((int)((p.X - minX) * invCellSize), 0, cols - 1);
			var cy = Math.Clamp((int)((p.Y - minY) * invCellSize), 0, rows - 1);
			var cz = Math.Clamp((int)((p.Z - minZ) * invCellSize), 0, depths - 1);

			// per-entity neighbor range clamp based on size relative to cell
			int range;
			if(body1.r <= 0.5 * cellSize)
				range = 1;
			else if(body1.r <= 1.5 * cellSize)
				range = 2;
			else
				range = (int)Math.Ceiling(body1.r * invCellSize) + 1;

			// Cache body1 properties for inner loop
			var body1PosX = body1.Position.X;
			var body1PosY = body1.Position.Y;
			var body1PosZ = body1.Position.Z;
			var body1R = body1.r;

			// 3D neighbor cell iteration
			for(var dz = -range; dz <= range; dz++)
			{
				var zz = cz + dz;
				if((uint)zz >= (uint)depths)
					continue;

				for(var dy = -range; dy <= range; dy++)
				{
					var yy = cy + dy;
					if((uint)yy >= (uint)rows)
						continue;

					for(var dx = -range; dx <= range; dx++)
					{
						var xx = cx + dx;
						if((uint)xx >= (uint)cols)
							continue;

						// Half-space de-dup: only process cells "ahead" in the 3D ordering
						// Compare (zz, yy, xx) > (cz, cy, cx) lexicographically
						if(zz < cz)
							continue;
						if(zz == cz && yy < cy)
							continue;
						if(zz == cz && yy == cy && xx < cx)
							continue;

						var bucket = buckets[zz * colsRows + yy * cols + xx];
						if(bucket == null)
							continue;

						var bucketCount = bucket.Count;
						for(var bi = 0; bi < bucketCount; bi++)
						{
							var j = bucket[bi];

							// within same cell ensure j>i to avoid duplicates
							if(xx == cx && yy == cy && zz == cz && j <= i)
								continue;

							var body2 = bodies[j];

							if(body2.IsAbsorbed)
								continue;

							// Fast AABB rejection using radii
							var sumR = body1R + body2.r;
							var diffX = body1PosX - body2.Position.X;

							if(Math.Abs(diffX) > sumR)
								continue;

							var diffY = body1PosY - body2.Position.Y;

							if(Math.Abs(diffY) > sumR)
								continue;

							var diffZ = body1PosZ - body2.Position.Z;

							if(Math.Abs(diffZ) > sumR)
								continue;

							var distSq = diffX * diffX + diffY * diffY + diffZ * diffZ;

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

		// Return pooled array
		activePool.Return(activeIndices);
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
