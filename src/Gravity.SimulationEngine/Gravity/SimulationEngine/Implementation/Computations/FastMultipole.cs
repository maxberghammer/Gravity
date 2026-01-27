using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Computations;

/// <summary>
/// Fast Multipole Method (FMM) for O(N) gravity computation.
/// 
/// Uses multipole expansion to approximate far-field interactions:
/// - Upward pass: aggregate multipoles from leaves to root
/// - Downward pass: propagate local expansions from root to leaves
/// - Direct: compute near-field exactly
/// 
/// Expansion order P=2 (monopole + quadrupole) for good accuracy/speed tradeoff.
/// </summary>
internal sealed class FastMultipole : SimulationEngine.IComputation
{
	private const double _eps = 1e-12;
	private const int _maxBodiesPerLeaf = 32;
	private const int _maxDepth = 10;
	private const double _theta = 0.5; // Opening angle for well-separated criterion

	void SimulationEngine.IComputation.Compute(IWorld world, IReadOnlyList<Body> allBodies, IReadOnlyList<Body> bodiesToUpdate, Diagnostics diagnostics)
	{
		var nBodies = allBodies.Count;
		if (nBodies == 0) return;

		// Zero accelerations and count active
		var activeCount = 0;
		for (var i = 0; i < nBodies; i++)
		{
			var b = allBodies[i];
			if (b.IsAbsorbed) continue;
			b.a = Vector3D.Zero;
			activeCount++;
		}

		if (activeCount == 0) return;

		// For small N, use direct
		if (activeCount <= 64)
		{
			ComputeDirect(allBodies);
			diagnostics.SetField("Strategy", "FMM-Direct");
			diagnostics.SetField("Bodies", activeCount);
			return;
		}

		// Build octree
		var root = BuildOctree(allBodies);

		// Upward pass: compute multipoles
		ComputeMultipoles(root, allBodies);

		// Downward pass + direct evaluation - only update bodiesToUpdate
		EvaluateForces(root, bodiesToUpdate);

		// Count cells
		var cellCount = CountCells(root);

		diagnostics.SetField("Strategy", "FMM");
		diagnostics.SetField("Bodies", activeCount);
		diagnostics.SetField("Cells", cellCount);
		diagnostics.SetField("MaxDepth", GetMaxDepth(root));
	}

	#region Octree

	private sealed class Cell
	{
		public Vector3D Center;
		public double Size;
		public int Depth;

		// Indices into bodies array
		public List<int>? BodyIndices;

		// Children (8 octants, null if leaf)
		public Cell?[]? Children;

		// Multipole expansion (monopole + quadrupole)
		public double Mass;           // Monopole
		public Vector3D CenterOfMass; // Monopole center
		public double Qxx, Qyy, Qzz;  // Quadrupole diagonal
		public double Qxy, Qxz, Qyz;  // Quadrupole off-diagonal

		public bool IsLeaf => Children == null;
	}

	private static Cell BuildOctree(IReadOnlyList<Body> bodies)
	{
		// Compute bounding box
		double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

		var indices = new List<int>(bodies.Count);
		for (var i = 0; i < bodies.Count; i++)
		{
			var b = bodies[i];
			if (b.IsAbsorbed) continue;
			indices.Add(i);
			var p = b.Position;
			if (p.X < minX) minX = p.X;
			if (p.Y < minY) minY = p.Y;
			if (p.Z < minZ) minZ = p.Z;
			if (p.X > maxX) maxX = p.X;
			if (p.Y > maxY) maxY = p.Y;
			if (p.Z > maxZ) maxZ = p.Z;
		}

		var size = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
		size = Math.Max(size, _eps) * 1.01;

		var center = new Vector3D(
			(minX + maxX) * 0.5,
			(minY + maxY) * 0.5,
			(minZ + maxZ) * 0.5);

		var root = new Cell
		{
			Center = center,
			Size = size,
			Depth = 0,
			BodyIndices = indices
		};

		Subdivide(root, bodies);
		return root;
	}

	private static void Subdivide(Cell cell, IReadOnlyList<Body> bodies)
	{
		if (cell.BodyIndices == null || cell.BodyIndices.Count <= _maxBodiesPerLeaf || cell.Depth >= _maxDepth)
			return;

		cell.Children = new Cell[8];
		var halfSize = cell.Size * 0.5;
		var quarterSize = cell.Size * 0.25;

		for (var octant = 0; octant < 8; octant++)
		{
			var ox = (octant & 1) == 0 ? -quarterSize : quarterSize;
			var oy = (octant & 2) == 0 ? -quarterSize : quarterSize;
			var oz = (octant & 4) == 0 ? -quarterSize : quarterSize;

			cell.Children[octant] = new Cell
			{
				Center = cell.Center + new Vector3D(ox, oy, oz),
				Size = halfSize,
				Depth = cell.Depth + 1,
				BodyIndices = []
			};
		}

		// Distribute bodies to children
		foreach (var idx in cell.BodyIndices)
		{
			var p = bodies[idx].Position;
			var octant = GetOctant(p, cell.Center);
			cell.Children[octant]!.BodyIndices!.Add(idx);
		}

		// Clear parent's body list (not a leaf anymore)
		cell.BodyIndices = null;

		// Recursively subdivide children
		foreach (var child in cell.Children)
		{
			if (child!.BodyIndices!.Count > 0)
				Subdivide(child, bodies);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetOctant(Vector3D pos, Vector3D center)
	{
		var octant = 0;
		if (pos.X >= center.X) octant |= 1;
		if (pos.Y >= center.Y) octant |= 2;
		if (pos.Z >= center.Z) octant |= 4;
		return octant;
	}

	#endregion

	#region Multipole Computation (Upward Pass)

	private static void ComputeMultipoles(Cell cell, IReadOnlyList<Body> bodies)
	{
		if (cell.IsLeaf)
		{
			// Compute multipole directly from bodies
			ComputeLeafMultipole(cell, bodies);
		}
		else
		{
			// Compute children first
			for (var i = 0; i < cell.Children!.Length; i++)
			{
				var child = cell.Children[i];
				if (child != null)
					ComputeMultipoles(child, bodies);
			}

			// Aggregate from children (M2M translation)
			AggregateMultipoles(cell);
		}
	}

	private static void ComputeLeafMultipole(Cell cell, IReadOnlyList<Body> bodies)
	{
		if (cell.BodyIndices == null || cell.BodyIndices.Count == 0)
		{
			cell.Mass = 0;
			return;
		}

		// Monopole
		var totalMass = 0.0;
		var comX = 0.0;
		var comY = 0.0;
		var comZ = 0.0;

		foreach (var idx in cell.BodyIndices)
		{
			var b = bodies[idx];
			var m = b.m;
			totalMass += m;
			comX += m * b.Position.X;
			comY += m * b.Position.Y;
			comZ += m * b.Position.Z;
		}

		if (totalMass < _eps)
		{
			cell.Mass = 0;
			return;
		}

		cell.Mass = totalMass;
		cell.CenterOfMass = new Vector3D(comX / totalMass, comY / totalMass, comZ / totalMass);

		// Quadrupole (traceless)
		var qxx = 0.0;
		var qyy = 0.0;
		var qzz = 0.0;
		var qxy = 0.0;
		var qxz = 0.0;
		var qyz = 0.0;

		foreach (var idx in cell.BodyIndices)
		{
			var b = bodies[idx];
			var m = b.m;
			var dx = b.Position.X - cell.CenterOfMass.X;
			var dy = b.Position.Y - cell.CenterOfMass.Y;
			var dz = b.Position.Z - cell.CenterOfMass.Z;
			var r2 = dx * dx + dy * dy + dz * dz;

			qxx += m * (3 * dx * dx - r2);
			qyy += m * (3 * dy * dy - r2);
			qzz += m * (3 * dz * dz - r2);
			qxy += m * 3 * dx * dy;
			qxz += m * 3 * dx * dz;
			qyz += m * 3 * dy * dz;
		}

		cell.Qxx = qxx;
		cell.Qyy = qyy;
		cell.Qzz = qzz;
		cell.Qxy = qxy;
		cell.Qxz = qxz;
		cell.Qyz = qyz;
	}

	private static void AggregateMultipoles(Cell cell)
	{
		// M2M: shift and combine child multipoles
		var totalMass = 0.0;
		var comX = 0.0;
		var comY = 0.0;
		var comZ = 0.0;

		foreach (var child in cell.Children!)
		{
			if (child == null || child.Mass < _eps) continue;
			totalMass += child.Mass;
			comX += child.Mass * child.CenterOfMass.X;
			comY += child.Mass * child.CenterOfMass.Y;
			comZ += child.Mass * child.CenterOfMass.Z;
		}

		if (totalMass < _eps)
		{
			cell.Mass = 0;
			return;
		}

		cell.Mass = totalMass;
		cell.CenterOfMass = new Vector3D(comX / totalMass, comY / totalMass, comZ / totalMass);

		// Aggregate quadrupoles with parallel axis theorem
		var qxx = 0.0;
		var qyy = 0.0;
		var qzz = 0.0;
		var qxy = 0.0;
		var qxz = 0.0;
		var qyz = 0.0;

		foreach (var child in cell.Children)
		{
			if (child == null || child.Mass < _eps) continue;

			var dx = child.CenterOfMass.X - cell.CenterOfMass.X;
			var dy = child.CenterOfMass.Y - cell.CenterOfMass.Y;
			var dz = child.CenterOfMass.Z - cell.CenterOfMass.Z;
			var r2 = dx * dx + dy * dy + dz * dz;
			var m = child.Mass;

			// Parallel axis theorem for quadrupole
			qxx += child.Qxx + m * (3 * dx * dx - r2);
			qyy += child.Qyy + m * (3 * dy * dy - r2);
			qzz += child.Qzz + m * (3 * dz * dz - r2);
			qxy += child.Qxy + m * 3 * dx * dy;
			qxz += child.Qxz + m * 3 * dx * dz;
			qyz += child.Qyz + m * 3 * dy * dz;
		}

		cell.Qxx = qxx;
		cell.Qyy = qyy;
		cell.Qzz = qzz;
		cell.Qxy = qxy;
		cell.Qxz = qxz;
		cell.Qyz = qyz;
	}

	#endregion

	#region Force Evaluation

	private static void EvaluateForces(Cell root, IReadOnlyList<Body> bodies)
	{
		// For each body, traverse tree and accumulate forces
		Parallel.For(0, bodies.Count, i =>
		{
			var b = bodies[i];
			if (b.IsAbsorbed) return;

			var acc = EvaluateForBody(root, b.Position, i, bodies);
			b.a = acc;
		});
	}

	private static Vector3D EvaluateForBody(Cell cell, Vector3D pos, int bodyIdx, IReadOnlyList<Body> bodies)
	{
		if (cell.Mass < _eps)
			return Vector3D.Zero;

		// Distance to cell center
		var r = pos - cell.CenterOfMass;
		var dist = r.Length;

		// Well-separated criterion: d/size > 1/theta
		if (dist > cell.Size / _theta && !cell.IsLeaf)
		{
			// Use multipole approximation
			return ComputeMultipoleForce(cell, pos);
		}

		if (cell.IsLeaf)
		{
			// Direct computation
			var acc = Vector3D.Zero;
			if (cell.BodyIndices != null)
			{
				foreach (var idx in cell.BodyIndices)
				{
					if (idx == bodyIdx) continue;
					var other = bodies[idx];
					var rv = other.Position - pos;
					var d2 = rv.LengthSquared;
					if (d2 < _eps) continue;
					var d = Math.Sqrt(d2);
					var f = IWorld.G * other.m / (d2 * d);
					acc += f * rv;
				}
			}
			return acc;
		}

		// Recurse into children
		var result = Vector3D.Zero;
		for (var i = 0; i < cell.Children!.Length; i++)
		{
			var child = cell.Children[i];
			if (child != null)
				result += EvaluateForBody(child, pos, bodyIdx, bodies);
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3D ComputeMultipoleForce(Cell cell, Vector3D pos)
	{
		var r = pos - cell.CenterOfMass;
		var d2 = r.LengthSquared;
		if (d2 < _eps) return Vector3D.Zero;

		var d = Math.Sqrt(d2);
		var invD = 1.0 / d;
		var invD2 = invD * invD;
		var invD3 = invD2 * invD;
		var invD5 = invD3 * invD2;

		// Monopole contribution: F = -G*M*r/r³
		var mono = -IWorld.G * cell.Mass * invD3;
		var ax = mono * r.X;
		var ay = mono * r.Y;
		var az = mono * r.Z;

		// Quadrupole contribution (second-order correction)
		// F_quad = -G * (Q·r)/r⁵ * (5(r·Q·r)/r² - 2) * r + simpler terms
		var rx = r.X;
		var ry = r.Y;
		var rz = r.Z;

		// Q·r
		var qrx = cell.Qxx * rx + cell.Qxy * ry + cell.Qxz * rz;
		var qry = cell.Qxy * rx + cell.Qyy * ry + cell.Qyz * rz;
		var qrz = cell.Qxz * rx + cell.Qyz * ry + cell.Qzz * rz;

		// r·Q·r
		var rQr = rx * qrx + ry * qry + rz * qrz;

		// Quadrupole force factor
		var qFactor = IWorld.G * 0.5 * invD5;
		var rQrTerm = 5.0 * rQr * invD2;

		ax += qFactor * (qrx - rQrTerm * rx);
		ay += qFactor * (qry - rQrTerm * ry);
		az += qFactor * (qrz - rQrTerm * rz);

		return new Vector3D(ax, ay, az);
	}

	#endregion

	#region Helpers

	private static void ComputeDirect(IReadOnlyList<Body> bodies)
	{
		var n = bodies.Count;
		for (var i = 0; i < n; i++)
		{
			var bi = bodies[i];
			if (bi.IsAbsorbed) continue;
			for (var j = i + 1; j < n; j++)
			{
				var bj = bodies[j];
				if (bj.IsAbsorbed) continue;
				var r = bj.Position - bi.Position;
				var d2 = r.LengthSquared;
				if (d2 < _eps) continue;
				var d = Math.Sqrt(d2);
				var f = IWorld.G / (d2 * d);
				bi.a += f * bj.m * r;
				bj.a -= f * bi.m * r;
			}
		}
	}

	private static int CountCells(Cell cell)
	{
		var count = 1;
		if (cell.Children != null)
		{
			for (var i = 0; i < cell.Children.Length; i++)
			{
				var child = cell.Children[i];
				if (child != null)
					count += CountCells(child);
			}
		}
		return count;
	}

	private static int GetMaxDepth(Cell cell)
	{
		if (cell.Children == null)
			return cell.Depth;

		var max = cell.Depth;
		for (var i = 0; i < cell.Children.Length; i++)
		{
			var child = cell.Children[i];
			if (child != null)
				max = Math.Max(max, GetMaxDepth(child));
		}
		return max;
	}

	#endregion
}
