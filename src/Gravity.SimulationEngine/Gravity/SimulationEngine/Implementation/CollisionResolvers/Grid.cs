using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Gravity.SimulationEngine.Implementation.CollisionResolvers;

/// <summary>
/// High-performance collision resolver using uniform spatial grid with sparse clearing.
/// </summary>
internal sealed class Grid : SimulationEngine.ICollisionResolver
{
	#region Fields

	private const double MinDistance = 1e-10;

	// Persistent arrays - reused across frames
	private int[] _cellCount = [];
	private int[] _cellStart = [];
	private int[] _sorted = [];
	private int[] _usedCells = [];
	private int _usedCellCount;

	#endregion

	#region Implementation of ICollisionResolver

	void SimulationEngine.ICollisionResolver.ResolveCollisions(IWorld world, IReadOnlyList<Body> bodies, Diagnostics diagnostics)
	{
		var n = bodies.Count;
		if (n <= 1)
			return;

		// Step 1: Collect active bodies and compute bounds
		var activePool = ArrayPool<int>.Shared;
		var activeIndices = activePool.Rent(n);
		var activeCount = 0;

		double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
		double maxRadius = 0;

		for (var i = 0; i < n; i++)
		{
			var body = bodies[i];
			if (body.IsAbsorbed)
				continue;

			activeIndices[activeCount++] = i;
			var p = body.Position;

			if (p.X < minX) minX = p.X;
			if (p.Y < minY) minY = p.Y;
			if (p.Z < minZ) minZ = p.Z;
			if (p.X > maxX) maxX = p.X;
			if (p.Y > maxY) maxY = p.Y;
			if (p.Z > maxZ) maxZ = p.Z;
			if (body.r > maxRadius) maxRadius = body.r;
		}

		if (activeCount <= 1)
		{
			activePool.Return(activeIndices);
			return;
		}

		// Step 2: Compute grid parameters
		var cellSize = Math.Max(1e-9, 2.0 * maxRadius);
		var invCellSize = 1.0 / cellSize;

		minX -= cellSize;
		minY -= cellSize;
		minZ -= cellSize;

		var cols = Math.Max(1, (int)Math.Ceiling((maxX - minX) * invCellSize) + 2);
		var rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) * invCellSize) + 2);
		var depths = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) * invCellSize) + 2);
		var colsRows = cols * rows;
		var gridSize = colsRows * depths;

		// Ensure capacity
		EnsureCapacity(gridSize, activeCount);

		// Sparse clear: only reset cells used last frame
		for (var i = 0; i < _usedCellCount; i++)
			_cellCount[_usedCells[i]] = 0;
		_usedCellCount = 0;

		// Step 3: Count bodies per cell
		for (var idx = 0; idx < activeCount; idx++)
		{
			var cellKey = GetCellKey(bodies[activeIndices[idx]].Position, minX, minY, minZ, invCellSize, cols, colsRows, gridSize);
			if (cellKey >= 0)
			{
				if (_cellCount[cellKey] == 0)
					_usedCells[_usedCellCount++] = cellKey;
				_cellCount[cellKey]++;
			}
		}

		// Step 4: Prefix sum for cell starts
		var total = 0;
		for (var i = 0; i < _usedCellCount; i++)
		{
			var cellKey = _usedCells[i];
			_cellStart[cellKey] = total;
			total += _cellCount[cellKey];
			_cellCount[cellKey] = 0;
		}

		// Step 5: Sort bodies into cells
		for (var idx = 0; idx < activeCount; idx++)
		{
			var bodyIdx = activeIndices[idx];
			var cellKey = GetCellKey(bodies[bodyIdx].Position, minX, minY, minZ, invCellSize, cols, colsRows, gridSize);
			if (cellKey >= 0)
			{
				_sorted[_cellStart[cellKey] + _cellCount[cellKey]++] = bodyIdx;
			}
		}

		// Step 6: Collision detection
		var elastic = world.ElasticCollisions;

		for (var idx = 0; idx < activeCount; idx++)
		{
			var i = activeIndices[idx];
			if (bodies[i].IsAbsorbed)
				continue;

			var body1 = bodies[i];
			var p1 = body1.Position;
			var cx = (int)((p1.X - minX) * invCellSize);
			var cy = (int)((p1.Y - minY) * invCellSize);
			var cz = (int)((p1.Z - minZ) * invCellSize);

			for (var dz = -1; dz <= 1; dz++)
			{
				var zz = cz + dz;
				if ((uint)zz >= (uint)depths) continue;

				for (var dy = -1; dy <= 1; dy++)
				{
					var yy = cy + dy;
					if ((uint)yy >= (uint)rows) continue;

					for (var dx = -1; dx <= 1; dx++)
					{
						var xx = cx + dx;
						if ((uint)xx >= (uint)cols) continue;

						var cellKey = zz * colsRows + yy * cols + xx;
						if ((uint)cellKey >= (uint)gridSize) continue;

						var count = _cellCount[cellKey];
						if (count == 0) continue;

						var start = _cellStart[cellKey];
						for (var bi = 0; bi < count; bi++)
						{
							var j = _sorted[start + bi];
							if (j <= i) continue;

							var body2 = bodies[j];
							if (body2.IsAbsorbed) continue;

							ProcessCollision(body1, body2, elastic);
						}
					}
				}
			}
		}

		activePool.Return(activeIndices);
	}

	#endregion

	#region Implementation

	private void EnsureCapacity(int gridSize, int bodyCount)
	{
		if (_cellCount.Length < gridSize)
		{
			var newSize = Math.Max(gridSize, 1024);
			_cellCount = new int[newSize];
			_cellStart = new int[newSize];
		}
		if (_sorted.Length < bodyCount)
			_sorted = new int[Math.Max(bodyCount, 256)];
		if (_usedCells.Length < bodyCount)
			_usedCells = new int[Math.Max(bodyCount, 256)];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetCellKey(Vector3D p, double minX, double minY, double minZ,
								   double invCellSize, int cols, int colsRows, int gridSize)
	{
		var cx = (int)((p.X - minX) * invCellSize);
		var cy = (int)((p.Y - minY) * invCellSize);
		var cz = (int)((p.Z - minZ) * invCellSize);
		var key = cz * colsRows + cy * cols + cx;
		return (uint)key < (uint)gridSize ? key : -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ProcessCollision(Body body1, Body body2, bool elastic)
	{
		if (body1.IsAbsorbed || body2.IsAbsorbed)
			return;

		var delta = body1.Position - body2.Position;
		var distSq = delta.LengthSquared;
		var sumRadii = body1.r + body2.r;

		if (distSq >= sumRadii * sumRadii)
			return;

		var dist = Math.Max(Math.Sqrt(distSq), MinDistance);
		var normal = delta / dist;

		if (elastic)
			ApplyElasticCollision(body1, body2, normal);
		else
			ApplyMergeCollision(body1, body2);
	}

	private static void ApplyElasticCollision(Body body1, Body body2, Vector3D normal)
	{
		var m1 = body1.m;
		var m2 = body2.m;
		var v1 = body1.v;
		var v2 = body2.v;

		var relVel = v1 - v2;
		var velAlongNormal = relVel * normal;

		// Only resolve if bodies are approaching
		if (velAlongNormal > 0)
			return;

		// Standard elastic collision impulse formula
		var impulseScalar = -2.0 * velAlongNormal / (1.0 / m1 + 1.0 / m2);
		var impulse = impulseScalar * normal;

		body1.v = v1 + impulse / m1;
		body2.v = v2 - impulse / m2;
		
		// Note: We intentionally do NOT separate bodies here.
		// Separation adds potential energy without removing kinetic energy,
		// which can cause energy gain over time. The gravity calculation
		// already clamps minimum distance, so close bodies won't cause
		// singularities.
	}

	private static void ApplyMergeCollision(Body body1, Body body2)
	{
		var m1 = body1.m;
		var m2 = body2.m;
		var vMerged = (m1 * body1.v + m2 * body2.v) / (m1 + m2);

		if (m2 > m1)
		{
			body2.Absorb(body1);
			body2.v = vMerged;
		}
		else
		{
			body1.Absorb(body2);
			body1.v = vMerged;
		}
	}

	#endregion
}
