using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Gravity.SimulationEngine.Implementation.Computations;

/// <summary>
/// P³M (Particle-Particle-Particle-Mesh) 3D computation.
/// 
/// Combines the accuracy of direct N-body for close encounters with the
/// efficiency of FFT-based mesh methods for long-range forces.
/// 
/// Uses proper Ewald splitting where:
/// - F_total = F_short + F_long
/// - F_short(r) = (G/r²) * erfc(α·r)  [computed directly for r &lt; rCut]
/// - F_long(r) = (G/r²) * erf(α·r)    [computed via FFT with exp(-k²/(4α²)) filter]
/// 
/// Since erfc(x) + erf(x) = 1, the sum exactly equals the full gravitational force.
/// 
/// Complexity: O(N + N_neighbors + GridSize³ log GridSize)
/// </summary>
internal sealed class ParticleMesh : SimulationEngine.IComputation
{
	#region Fields

	private const double _eps = 1e-12;
	private const int _maxGridSize = 64;
	private const int _minGridSize = 8;
	private const double _cutoffCells = 4.0; // Short-range cutoff in grid cells

	#endregion

	#region Implementation of IComputation

	/// <inheritdoc/>
	void SimulationEngine.IComputation.Compute(IWorld world, IReadOnlyList<Body> bodies, Diagnostics diagnostics)
	{
		var nBodies = bodies.Count;

		if (nBodies == 0)
			return;

		// Count active bodies and compute 3D bounding box
		var (minBound, maxBound, activeCount) = ComputeBoundingBox(bodies);

		if (activeCount == 0)
			return;

		// Make cubic domain with padding
		var span = maxBound - minBound;
		var maxSpan = Math.Max(span.X, Math.Max(span.Y, span.Z));
		maxSpan = Math.Max(maxSpan, _eps) * 1.3;

		var center = (minBound + maxBound) / 2;
		var halfSpan = maxSpan / 2;
		minBound = center - new Vector3D(halfSpan, halfSpan, halfSpan);

		// Adaptive grid size
		var n = ComputeAdaptiveGridSize(activeCount);
		var h = maxSpan / n;

		// For few bodies, make cutoff cover the entire domain so all forces are computed directly
		// This ensures accuracy for small N while still using P³M structure
		var rCut = activeCount <= 32
			? maxSpan * 2  // Cover entire domain - all short-range
			: _cutoffCells * h;

		// Ewald splitting parameter: α = 1/(√2·σ) where σ = rCut/3
		// This gives erfc(α·rCut) ≈ 0.01, so short-range contribution is negligible beyond rCut
		var sigma = rCut / 3.0;
		var alpha = 1.0 / (Math.Sqrt(2.0) * sigma);

		// Initialize accelerations to zero
		for (var i = 0; i < nBodies; i++)
			if (!bodies[i].IsAbsorbed)
				bodies[i].a = Vector3D.Zero;

		// Step 1: Compute short-range forces directly with erfc splitting
		var shortRangePairs = ComputeShortRangeForces(bodies, rCut, minBound, alpha);

		// Step 2: Compute long-range forces using PM with erf splitting (Gaussian filter)
		// For few bodies with large rCut, this will contribute almost nothing (as intended)
		ComputeLongRangeForces(bodies, n, h, minBound, sigma);

		diagnostics.SetField("Strategy", "Particle-Mesh-P3M-Ewald");
		diagnostics.SetField("GridSize", n);
		diagnostics.SetField("CutoffRadius", rCut);
		diagnostics.SetField("Alpha", alpha);
		diagnostics.SetField("ShortRangePairs", shortRangePairs);
		diagnostics.SetField("Bodies", activeCount);
	}

	#endregion

	#region Short-Range Forces

	/// <summary>
	/// Compute short-range forces using direct calculation with spatial hashing.
	/// Uses Ewald splitting: F_short = F_full * erfc(α·r)
	/// </summary>
	private static int ComputeShortRangeForces(IReadOnlyList<Body> bodies, double rCut, Vector3D minBound, double alpha)
	{
		var rCut2 = rCut * rCut;
		var pairCount = 0;

		// Build spatial hash grid
		var cellSize = rCut;
		var invCellSize = 1.0 / cellSize;
		var grid = new Dictionary<(int, int, int), List<int>>();

		for (var i = 0; i < bodies.Count; i++)
		{
			var body = bodies[i];
			if (body.IsAbsorbed) continue;

			var cell = GetCell(body.Position, minBound, invCellSize);
			if (!grid.TryGetValue(cell, out var list))
			{
				list = [];
				grid[cell] = list;
			}
			list.Add(i);
		}

		// Process neighbor pairs
		Parallel.ForEach(grid, kvp =>
		{
			var (cx, cy, cz) = kvp.Key;
			var cellBodies = kvp.Value;

			for (var dx = -1; dx <= 1; dx++)
			{
				for (var dy = -1; dy <= 1; dy++)
				{
					for (var dz = -1; dz <= 1; dz++)
					{
						var neighborCell = (cx + dx, cy + dy, cz + dz);
						if (!grid.TryGetValue(neighborCell, out var neighbors))
							continue;

						foreach (var i in cellBodies)
						{
							var bi = bodies[i];

							foreach (var j in neighbors)
							{
								if (neighborCell == kvp.Key && j <= i) continue;

								var bj = bodies[j];
								var r = bj.Position - bi.Position;
								var dist2 = r.LengthSquared;

								if (dist2 >= rCut2 || dist2 < _eps) continue;

								var dist = Math.Sqrt(dist2);

								// Ewald short-range: G/r² * erfc(α·r)
								var erfcFactor = SpecialFunctions.Erfc(alpha * dist);
								var force = IWorld.G * erfcFactor / (dist2 * dist);

								var ai = force * bj.m * r;
								var aj = force * bi.m * r;

								lock (bi) { bi.a += ai; }
								lock (bj) { bj.a -= aj; }

								Interlocked.Increment(ref pairCount);
							}
						}
					}
				}
			}
		});

		return pairCount;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static (int, int, int) GetCell(Vector3D pos, Vector3D minBound, double invCellSize)
	{
		return (
			(int)Math.Floor((pos.X - minBound.X) * invCellSize),
			(int)Math.Floor((pos.Y - minBound.Y) * invCellSize),
			(int)Math.Floor((pos.Z - minBound.Z) * invCellSize)
		);
	}

	#endregion

	#region Long-Range Forces

	/// <summary>
	/// Compute long-range forces using PM with Gaussian filter (erf complement).
	/// The filter exp(-k²σ²/2) in k-space corresponds to erf(r/(√2·σ)) in real space.
	/// </summary>
	private static void ComputeLongRangeForces(IReadOnlyList<Body> bodies, int n, double h, Vector3D minBound, double sigma)
	{
		var gridSize = n * n * n;

		var massGrid = new double[gridSize];
		var accXGrid = new double[gridSize];
		var accYGrid = new double[gridSize];
		var accZGrid = new double[gridSize];

		AssignMassToGrid3D(bodies, massGrid, n, minBound, h);
		SolveLongRangeFFT3D(massGrid, accXGrid, accYGrid, accZGrid, n, h, sigma);
		InterpolateAndAddAcceleration(bodies, accXGrid, accYGrid, accZGrid, n, minBound, h);
	}

	private static void AssignMassToGrid3D(IReadOnlyList<Body> bodies, double[] grid, int n, Vector3D minBound, double h)
	{
		var invH = 1.0 / h;

		foreach (var body in bodies)
		{
			if (body.IsAbsorbed) continue;

			var px = (body.Position.X - minBound.X) * invH - 0.5;
			var py = (body.Position.Y - minBound.Y) * invH - 0.5;
			var pz = (body.Position.Z - minBound.Z) * invH - 0.5;

			var i0 = (int)Math.Floor(px);
			var j0 = (int)Math.Floor(py);
			var k0 = (int)Math.Floor(pz);

			var dx = px - i0;
			var dy = py - j0;
			var dz = pz - k0;

			for (var di = 0; di <= 1; di++)
			{
				var i = i0 + di;
				if (i < 0 || i >= n) continue;
				var wx = di == 0 ? 1.0 - dx : dx;

				for (var dj = 0; dj <= 1; dj++)
				{
					var j = j0 + dj;
					if (j < 0 || j >= n) continue;
					var wy = dj == 0 ? 1.0 - dy : dy;

					for (var dk = 0; dk <= 1; dk++)
					{
						var k = k0 + dk;
						if (k < 0 || k >= n) continue;
						var wz = dk == 0 ? 1.0 - dz : dz;

						grid[(k * n + j) * n + i] += body.m * wx * wy * wz;
					}
				}
			}
		}
	}

	/// <summary>
	/// FFT solver with Gaussian filter: exp(-k²σ²/2)
	/// This corresponds to the erf part of the Ewald splitting in real space.
	/// </summary>
	private static void SolveLongRangeFFT3D(double[] massGrid, double[] accX, double[] accY, double[] accZ, int n, double h, double sigma)
	{
		var gridSize = n * n * n;
		var rhoK = new Complex[gridSize];

		for (var i = 0; i < gridSize; i++)
			rhoK[i] = new(massGrid[i], 0);

		FFT3D(rhoK, n, true);

		var axK = new Complex[gridSize];
		var ayK = new Complex[gridSize];
		var azK = new Complex[gridSize];

		var l = n * h;
		var dk = 2.0 * Math.PI / l;
		var sigma2 = sigma * sigma;

		// Factor for converting density to acceleration
		var factor = 4.0 * Math.PI * IWorld.G / (h * h * h * n * n * n);

		for (var kk = 0; kk < n; kk++)
		{
			var kz = kk <= n / 2 ? kk * dk : (kk - n) * dk;

			for (var jj = 0; jj < n; jj++)
			{
				var ky = jj <= n / 2 ? jj * dk : (jj - n) * dk;

				for (var ii = 0; ii < n; ii++)
				{
					var kx = ii <= n / 2 ? ii * dk : (ii - n) * dk;
					var k2 = kx * kx + ky * ky + kz * kz;

					var idx = (kk * n + jj) * n + ii;

					if (k2 > _eps)
					{
						// Gaussian filter: exp(-k²σ²/2) corresponds to erf in real space
						var longRangeFilter = Math.Exp(-k2 * sigma2 / 2);

						var rho = rhoK[idx];
						var iRho = new Complex(-rho.Imaginary, rho.Real);

						var coeff = factor * longRangeFilter / k2;
						axK[idx] = coeff * kx * iRho;
						ayK[idx] = coeff * ky * iRho;
						azK[idx] = coeff * kz * iRho;
					}
					else
					{
						axK[idx] = Complex.Zero;
						ayK[idx] = Complex.Zero;
						azK[idx] = Complex.Zero;
					}
				}
			}
		}

		FFT3D(axK, n, false);
		FFT3D(ayK, n, false);
		FFT3D(azK, n, false);

		for (var i = 0; i < gridSize; i++)
		{
			accX[i] = axK[i].Real;
			accY[i] = ayK[i].Real;
			accZ[i] = azK[i].Real;
		}
	}

	private static void FFT3D(Complex[] data, int n, bool forward)
	{
		// Transform along X
		Parallel.For(0, n * n, slice =>
		{
			var k = slice / n;
			var j = slice % n;
			var row = new Complex[n];

			for (var i = 0; i < n; i++)
				row[i] = data[(k * n + j) * n + i];

			if (forward)
				Fourier.Forward(row, FourierOptions.NoScaling);
			else
				Fourier.Inverse(row, FourierOptions.NoScaling);

			for (var i = 0; i < n; i++)
				data[(k * n + j) * n + i] = row[i];
		});

		// Transform along Y
		Parallel.For(0, n * n, slice =>
		{
			var k = slice / n;
			var i = slice % n;
			var col = new Complex[n];

			for (var j = 0; j < n; j++)
				col[j] = data[(k * n + j) * n + i];

			if (forward)
				Fourier.Forward(col, FourierOptions.NoScaling);
			else
				Fourier.Inverse(col, FourierOptions.NoScaling);

			for (var j = 0; j < n; j++)
				data[(k * n + j) * n + i] = col[j];
		});

		// Transform along Z
		Parallel.For(0, n * n, slice =>
		{
			var j = slice / n;
			var i = slice % n;
			var depth = new Complex[n];

			for (var k = 0; k < n; k++)
				depth[k] = data[(k * n + j) * n + i];

			if (forward)
				Fourier.Forward(depth, FourierOptions.NoScaling);
			else
				Fourier.Inverse(depth, FourierOptions.NoScaling);

			for (var k = 0; k < n; k++)
				data[(k * n + j) * n + i] = depth[k];
		});

		if (!forward)
		{
			var scale = 1.0 / (n * n * n);
			for (var i = 0; i < data.Length; i++)
				data[i] *= scale;
		}
	}

	private static void InterpolateAndAddAcceleration(
		IReadOnlyList<Body> bodies,
		double[] accX,
		double[] accY,
		double[] accZ,
		int n,
		Vector3D minBound,
		double h)
	{
		var invH = 1.0 / h;

		Parallel.For(0, bodies.Count, bodyIdx =>
		{
			var body = bodies[bodyIdx];
			if (body.IsAbsorbed) return;

			var px = (body.Position.X - minBound.X) * invH - 0.5;
			var py = (body.Position.Y - minBound.Y) * invH - 0.5;
			var pz = (body.Position.Z - minBound.Z) * invH - 0.5;

			var i0 = (int)Math.Floor(px);
			var j0 = (int)Math.Floor(py);
			var k0 = (int)Math.Floor(pz);

			var dx = px - i0;
			var dy = py - j0;
			var dz = pz - k0;

			var ax = 0.0;
			var ay = 0.0;
			var az = 0.0;

			for (var di = 0; di <= 1; di++)
			{
				var i = i0 + di;
				if (i < 0 || i >= n) continue;
				var wx = di == 0 ? 1.0 - dx : dx;

				for (var dj = 0; dj <= 1; dj++)
				{
					var j = j0 + dj;
					if (j < 0 || j >= n) continue;
					var wy = dj == 0 ? 1.0 - dy : dy;

					for (var dk = 0; dk <= 1; dk++)
					{
						var k = k0 + dk;
						if (k < 0 || k >= n) continue;
						var wz = dk == 0 ? 1.0 - dz : dz;

						var w = wx * wy * wz;
						var cellIdx = (k * n + j) * n + i;

						ax += w * accX[cellIdx];
						ay += w * accY[cellIdx];
						az += w * accZ[cellIdx];
					}
				}
			}

			body.a += new Vector3D(ax, ay, az);
		});
	}

	#endregion

	#region Helper Methods

	private static (Vector3D min, Vector3D max, int count) ComputeBoundingBox(IReadOnlyList<Body> bodies)
	{
		var minBound = new Vector3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
		var maxBound = new Vector3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
		var count = 0;

		foreach (var body in bodies)
		{
			if (body.IsAbsorbed) continue;
			count++;

			var p = body.Position;
			if (p.X < minBound.X) minBound = new(p.X, minBound.Y, minBound.Z);
			if (p.Y < minBound.Y) minBound = new(minBound.X, p.Y, minBound.Z);
			if (p.Z < minBound.Z) minBound = new(minBound.X, minBound.Y, p.Z);

			if (p.X > maxBound.X) maxBound = new(p.X, maxBound.Y, maxBound.Z);
			if (p.Y > maxBound.Y) maxBound = new(maxBound.X, p.Y, maxBound.Z);
			if (p.Z > maxBound.Z) maxBound = new(maxBound.X, maxBound.Y, p.Z);
		}

		if (count == 0 || double.IsInfinity(minBound.X))
		{
			minBound = new(-1, -1, -1);
			maxBound = new(1, 1, 1);
		}

		return (minBound, maxBound, count);
	}

	private static int ComputeAdaptiveGridSize(int bodyCount)
	{
		var n = (int)Math.Ceiling(Math.Pow(bodyCount, 1.0 / 3.0) * 2);
		n = Math.Max(_minGridSize, Math.Min(_maxGridSize, n));
		n = (int)Math.Pow(2, Math.Ceiling(Math.Log2(n)));
		return Math.Min(n, _maxGridSize);
	}

	#endregion
}
