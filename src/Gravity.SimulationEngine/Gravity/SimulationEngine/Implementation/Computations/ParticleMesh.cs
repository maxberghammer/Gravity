using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;

namespace Gravity.SimulationEngine.Implementation.Computations;

/// <summary>
/// Particle-Mesh (PM) computation using FFT
/// Uses 3D gravity (F ∝ 1/r²) projected onto 2D plane, matching Barnes-Hut physics.
/// Algorithm:
/// 1. Assign particle masses to grid using CIC interpolation
/// 2. FFT the mass grid to k-space
/// 3. Apply Green's function for 3D gravity: G(k) ∝ 1/|k| (not 1/k² like 2D!)
/// 4. Compute gradient in k-space: a(k) = -i·k·φ(k)
/// 5. Inverse FFT to get acceleration field
/// 6. Interpolate acceleration back to particles using CIC
/// Adaptive grid sizing ensures accuracy for both small and large N.
/// Complexity: O(N + GridSize² log GridSize)
/// </summary>
internal sealed class ParticleMesh : SimulationEngine.IComputation
{
	#region Fields

	private const double _eps = 1e-12;
	private const int _maxGridSize = 256;
	private const int _minGridSize = 32;
	private const int _targetCellsPerSeparation = 8; // Grid cells per typical body separation

	#endregion

	#region Implementation of IComputation

	/// <inheritdoc/>
	void SimulationEngine.IComputation.Compute(IWorld world, IReadOnlyList<Body> bodies, Diagnostics diagnostics)
	{
		var nBodies = bodies.Count;

		if(nBodies == 0)
			return;

		// Count active bodies and compute bounding box
		var xMin = double.PositiveInfinity;
		var yMin = double.PositiveInfinity;
		var xMax = double.NegativeInfinity;
		var yMax = double.NegativeInfinity;
		var activeCount = 0;

		for(var i = 0; i < nBodies; i++)
		{
			if(bodies[i].IsAbsorbed)
				continue;

			activeCount++;
			var p = bodies[i].Position;
			if(p.X < xMin)
				xMin = p.X;
			if(p.Y < yMin)
				yMin = p.Y;
			if(p.X > xMax)
				xMax = p.X;
			if(p.Y > yMax)
				yMax = p.Y;
		}

		if(activeCount == 0 ||
		   double.IsInfinity(xMin))
		{
			xMin = yMin = -1.0;
			xMax = yMax = 1.0;
		}

		// Compute minimum separation (sample first 100 body pairs)
		var minSep = ComputeMinSeparation(bodies, activeCount);

		// Add padding and make square
		var spanX = Math.Max(_eps, xMax - xMin);
		var spanY = Math.Max(_eps, yMax - yMin);
		var span = Math.Max(spanX, spanY) * 1.2; // 20% padding
		var centerX = (xMin + xMax) / 2;
		var centerY = (yMin + yMax) / 2;
		xMin = centerX - span / 2;
		yMin = centerY - span / 2;

		// Adaptive grid size: ensure enough cells to resolve minimum separation
		var n = ComputeAdaptiveGridSize(span, minSep, activeCount);
		var h = span / n;

		// Allocate grids
		var massGrid = new double[n * n];
		var accXGrid = new double[n * n];
		var accYGrid = new double[n * n];

		// Step 1: Assign mass to grid using CIC
		AssignMassToGrid(bodies, massGrid, n, xMin, yMin, h);

		// Step 2-5: Solve for acceleration using FFT
		SolveAccelerationFFT(massGrid, accXGrid, accYGrid, n, h);

		// Step 6: Interpolate acceleration to particles
		InterpolateAccelerationToParticles(bodies, accXGrid, accYGrid, n, xMin, yMin, h);

		diagnostics.SetField("Strategy", "Particle-Mesh-FFT");
		diagnostics.SetField("GridSize", n);
		diagnostics.SetField("GridSpacing", h);
		diagnostics.SetField("MinSeparation", minSep);
		diagnostics.SetField("CellsPerSep", minSep / h);
	}

	#endregion

	#region Implementation

	/// <summary>
	/// Compute minimum separation between bodies (sample first pairs for efficiency)
	/// </summary>
	private static double ComputeMinSeparation(IReadOnlyList<Body> bodies, int activeCount)
	{
		var minSep = double.PositiveInfinity;
		var sampleSize = Math.Min(activeCount, 50);
		var count = 0;

		for(var i = 0; i < bodies.Count && count < sampleSize; i++)
		{
			if(bodies[i].IsAbsorbed)
				continue;

			var innerCount = 0;

			for(var j = i + 1; j < bodies.Count && innerCount < sampleSize; j++)
			{
				if(bodies[j].IsAbsorbed)
					continue;

				var sep = (bodies[i].Position - bodies[j].Position).Length;
				if(sep > _eps &&
				   sep < minSep)
					minSep = sep;

				innerCount++;
			}

			count++;
		}

		return double.IsInfinity(minSep)
				   ? 1.0
				   : minSep;
	}

	/// <summary>
	/// Compute adaptive grid size based on domain size, minimum separation, and body count
	/// </summary>
	private static int ComputeAdaptiveGridSize(double span, double minSep, int activeCount)
	{
		// Target: TargetCellsPerSeparation grid cells per minimum separation
		var targetH = minSep / _targetCellsPerSeparation;
		var targetN = (int)Math.Ceiling(span / targetH);

		// Also consider body count - more bodies need finer grid
		var countBasedN = (int)Math.Sqrt(activeCount) * 4;

		// Take maximum and round to power of 2
		var n = Math.Max(targetN, countBasedN);
		n = Math.Max(_minGridSize, Math.Min(_maxGridSize, n));

		// Round up to next power of 2 for FFT efficiency
		n = (int)Math.Pow(2, Math.Ceiling(Math.Log2(n)));

		return Math.Min(n, _maxGridSize);
	}

	/// <summary>
	/// Assign particle masses to grid using CIC interpolation
	/// </summary>
	private static void AssignMassToGrid(IReadOnlyList<Body> bodies, double[] grid, int n, double xMin, double yMin, double h)
	{
		var invH = 1.0 / h;

		foreach(var body in bodies)
		{
			if(body.IsAbsorbed)
				continue;

			// Position in grid coordinates (cell-centered)
			var px = (body.Position.X - xMin) * invH - 0.5;
			var py = (body.Position.Y - yMin) * invH - 0.5;

			var i0 = (int)Math.Floor(px);
			var j0 = (int)Math.Floor(py);
			var dx = px - i0;
			var dy = py - j0;

			for(var di = 0; di <= 1; di++)
			{
				var i = i0 + di;

				if(i < 0 ||
				   i >= n)
					continue;

				var wx = di == 0
							 ? 1.0 - dx
							 : dx;

				for(var dj = 0; dj <= 1; dj++)
				{
					var j = j0 + dj;

					if(j < 0 ||
					   j >= n)
						continue;

					var wy = dj == 0
								 ? 1.0 - dy
								 : dy;

					grid[j * n + i] += body.m * wx * wy;
				}
			}
		}
	}

	/// <summary>
	/// Compute acceleration field using FFT.
	/// For 3D gravity (F = G*M/r²) in 2D Fourier space:
	/// The Green's function for the 3D Laplacian in 2D is G(k) = 1/|k|
	/// We solve: a = -G * ∇ ∫ ρ(r')/|r-r'| dr'
	/// In k-space: a(k) = -G * i*k * ρ(k) / |k|
	/// The discrete FFT includes implicit h² factors from the integration.
	/// After inverse FFT, we need to scale by h² to get correct units.
	/// </summary>
	private static void SolveAccelerationFFT(double[] massGrid, double[] accX, double[] accY, int n, double h)
	{
		// Convert mass grid to complex for FFT
		var rhoK = new Complex[n * n];
		for(var i = 0; i < n * n; i++)
			rhoK[i] = new(massGrid[i], 0);

		// Forward 2D FFT
		FFT2D(rhoK, n, true);

		// Compute acceleration in k-space
		var axK = new Complex[n * n];
		var ayK = new Complex[n * n];

		var l = n * h; // Domain size
		var dk = 2.0 * Math.PI / l;

		// The factor needs careful dimensional analysis:
		// For 3D gravity in 2D FFT, the correct scaling is:
		// -G / (h * n) where h*n = L (domain size)
		// This accounts for the discrete FFT normalization
		var factor = -IWorld.G / (h * n);

		for(var jj = 0; jj < n; jj++)
		{
			var ky = jj <= n / 2
						 ? jj * dk
						 : (jj - n) * dk;

			for(var ii = 0; ii < n; ii++)
			{
				var kx = ii <= n / 2
							 ? ii * dk
							 : (ii - n) * dk;
				var kMag = Math.Sqrt(kx * kx + ky * ky);

				var idx = jj * n + ii;

				if(kMag > _eps)
				{
					// a(k) = factor * i*k * ρ(k) / |k|
					var rho = rhoK[idx];
					var iRho = new Complex(-rho.Imaginary, rho.Real); // i * rho

					var coeff = factor / kMag;
					axK[idx] = coeff * kx * iRho;
					ayK[idx] = coeff * ky * iRho;
				}
				else
				{
					axK[idx] = Complex.Zero;
					ayK[idx] = Complex.Zero;
				}
			}
		}

		// Inverse 2D FFT
		FFT2D(axK, n, false);
		FFT2D(ayK, n, false);

		// Extract real parts
		for(var i = 0; i < n * n; i++)
		{
			accX[i] = axK[i].Real;
			accY[i] = ayK[i].Real;
		}
	}

	/// <summary>
	/// 2D FFT using separable 1D FFTs
	/// </summary>
	private static void FFT2D(Complex[] data, int n, bool forward)
	{
		// Transform rows
		Parallel.For(0, n, j =>
						   {
							   var row = new Complex[n];
							   for(var i = 0; i < n; i++)
								   row[i] = data[j * n + i];

							   if(forward)
								   Fourier.Forward(row, FourierOptions.NoScaling);
							   else
								   Fourier.Inverse(row, FourierOptions.NoScaling);

							   for(var i = 0; i < n; i++)
								   data[j * n + i] = row[i];
						   });

		// Transform columns
		Parallel.For(0, n, i =>
						   {
							   var col = new Complex[n];
							   for(var j = 0; j < n; j++)
								   col[j] = data[j * n + i];

							   if(forward)
								   Fourier.Forward(col, FourierOptions.NoScaling);
							   else
								   Fourier.Inverse(col, FourierOptions.NoScaling);

							   for(var j = 0; j < n; j++)
								   data[j * n + i] = col[j];
						   });

		// Apply normalization for inverse
		if(!forward)
		{
			var scale = 1.0 / (n * n);
			for(var i = 0; i < n * n; i++)
				data[i] *= scale;
		}
	}

	/// <summary>
	/// Interpolate acceleration from grid to particles using CIC.
	/// </summary>
	private static void InterpolateAccelerationToParticles(IReadOnlyList<Body> bodies,
														   double[] accX,
														   double[] accY,
														   int n,
														   double xMin,
														   double yMin,
														   double h)
	{
		var invH = 1.0 / h;

		Parallel.For(0,
					 bodies.Count,
					 bodyIdx =>
					 {
						 var body = bodies[bodyIdx];

						 if(body.IsAbsorbed)
							 return;

						 // Position in grid coordinates (cell-centered)
						 var px = (body.Position.X - xMin) * invH - 0.5;
						 var py = (body.Position.Y - yMin) * invH - 0.5;

						 var i0 = (int)Math.Floor(px);
						 var j0 = (int)Math.Floor(py);
						 var dx = px - i0;
						 var dy = py - j0;

						 var ax = 0.0;
						 var ay = 0.0;

						 for(var di = 0; di <= 1; di++)
						 {
							 var i = i0 + di;

							 if(i < 0 ||
								i >= n)
								 continue;

							 var wx = di == 0
										  ? 1.0 - dx
										  : dx;

							 for(var dj = 0; dj <= 1; dj++)
							 {
								 var j = j0 + dj;

								 if(j < 0 ||
									j >= n)
									 continue;

								 var wy = dj == 0
											  ? 1.0 - dy
											  : dy;

								 var w = wx * wy;
								 var cellIdx = j * n + i;
								 ax += w * accX[cellIdx];
								 ay += w * accY[cellIdx];
							 }
						 }

						 body.a = new(ax, ay, 0);
					 });
	}

	#endregion
}