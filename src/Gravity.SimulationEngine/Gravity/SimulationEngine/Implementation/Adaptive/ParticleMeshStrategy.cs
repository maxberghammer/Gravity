using System;
using System.Numerics;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

/// <summary>
/// Particle-Mesh-Ewald acceleration strategy with FFT (fixed numerics)
/// - Long-range: solve Poisson on a uniform grid in k-space
/// - Short-range: direct summation within cutoff
/// </summary>
internal sealed class ParticleMeshStrategy : IAccelerationStrategy
{
	#region Fields

	private const int DefaultGridSize = 128; // power-of-two for FFT
	private const double CutoffFactor = 2.5; // cutoffRadius = CutoffFactor * max(dx,dy)
	private const double Eps = 1e-12;

	#endregion

	#region Implementation

	public void ComputeAccelerations(Body[] bodies, Diagnostics diagnostics)
	{
		var nBodies = bodies.Length;
		if(nBodies == 0)
			return;

		// Compute bounding box over active bodies
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < nBodies; i++)
		{
			if(bodies[i].IsAbsorbed)
				continue;
			var p = bodies[i].Position;
			if(p.X < l) l = p.X;
			if(p.Y < t) t = p.Y;
			if(p.X > r) r = p.X;
			if(p.Y > b) b = p.Y;
		}

		if(double.IsInfinity(l) || double.IsInfinity(t) || double.IsInfinity(r) || double.IsInfinity(b))
		{
			l = t = -1.0; r = b = 1.0;
		}

		// Add 10% padding to mitigate boundary effects
		var pad = 0.1 * Math.Max(r - l, b - t);
		l -= pad; t -= pad; r += pad; b += pad;

		var gridSize = DefaultGridSize;
		var spanX = Math.Max(Eps, r - l);
		var spanY = Math.Max(Eps, b - t);
		var dx = spanX / gridSize;
		var dy = spanY / gridSize;
		var cellArea = dx * dy;
		var cutoffRadius = CutoffFactor * Math.Max(dx, dy);
		var cutoffSq = cutoffRadius * cutoffRadius;

		// Allocate grid (row-major 1D array) for mass density ρ = m / cellArea
		var rho = new Complex[gridSize * gridSize];

		// Cloud-in-cell (CIC) mass assignment to grid as density
		for(var idx = 0; idx < nBodies; idx++)
		{
			var body = bodies[idx];
			if(body.IsAbsorbed)
				continue;
			var gx = (body.Position.X - l) / dx;
			var gy = (body.Position.Y - t) / dy;
			var i0 = (int)Math.Floor(gx);
			var j0 = (int)Math.Floor(gy);
			var wx1 = gx - i0; var wx0 = 1.0 - wx1;
			var wy1 = gy - j0; var wy0 = 1.0 - wy1;
			var dens = body.m / cellArea;
			// accumulate to 4 neighbors
			if(i0 >= 0 && i0 < gridSize && j0 >= 0 && j0 < gridSize)
				rho[j0 * gridSize + i0] += dens * wx0 * wy0;
			if(i0 + 1 >= 0 && i0 + 1 < gridSize && j0 >= 0 && j0 < gridSize)
				rho[j0 * gridSize + (i0 + 1)] += dens * wx1 * wy0;
			if(i0 >= 0 && i0 < gridSize && j0 + 1 >= 0 && j0 + 1 < gridSize)
				rho[(j0 + 1) * gridSize + i0] += dens * wx0 * wy1;
			if(i0 + 1 >= 0 && i0 + 1 < gridSize && j0 + 1 >= 0 && j0 + 1 < gridSize)
				rho[(j0 + 1) * gridSize + (i0 + 1)] += dens * wx1 * wy1;
		}

		// 2D FFT of density (forward transform with no normalization)
		var data = (Complex[])rho.Clone();
		// rows
		Parallel.For(0, gridSize, row =>
		{
			var rowData = new Complex[gridSize];
			for(var col = 0; col < gridSize; col++) rowData[col] = data[row * gridSize + col];
			Fourier.Forward(rowData, FourierOptions.Default);
			for(var col = 0; col < gridSize; col++) data[row * gridSize + col] = rowData[col];
		});
		// cols
		Parallel.For(0, gridSize, col =>
		{
			var colData = new Complex[gridSize];
			for(var row = 0; row < gridSize; row++) colData[row] = data[row * gridSize + col];
			Fourier.Forward(colData, FourierOptions.Default);
			for(var row = 0; row < gridSize; row++) data[row * gridSize + col] = colData[row];
		});

		// Solve Poisson in k-space: φ(k) = -4πG * ρ(k) / |k|² (k≠0)
		var phiK = new Complex[gridSize * gridSize];
		var dkx = 2.0 * Math.PI / (gridSize * dx);
		var dky = 2.0 * Math.PI / (gridSize * dy);
		for(var i = 0; i < gridSize; i++)
		{
			var kx = i <= gridSize / 2 ? i * dkx : (i - gridSize) * dkx;
			for(var j = 0; j < gridSize; j++)
			{
				var ky = j <= gridSize / 2 ? j * dky : (j - gridSize) * dky;
				var k2 = kx * kx + ky * ky;
				phiK[i * gridSize + j] = k2 > Eps ? (-4.0 * Math.PI * IWorld.G) * data[i * gridSize + j] / k2 : Complex.Zero;
			}
		}

		// Inverse FFT to get φ in real space
		var phi = (Complex[])phiK.Clone();
		// rows
		Parallel.For(0, gridSize, row =>
		{
			var rowData = new Complex[gridSize];
			for(var col = 0; col < gridSize; col++) rowData[col] = phi[row * gridSize + col];
			Fourier.Inverse(rowData, FourierOptions.Default);
			for(var col = 0; col < gridSize; col++) phi[row * gridSize + col] = rowData[col];
		});
		// cols
		Parallel.For(0, gridSize, col =>
		{
			var colData = new Complex[gridSize];
			for(var row = 0; row < gridSize; row++) colData[row] = phi[row * gridSize + col];
			Fourier.Inverse(colData, FourierOptions.Default);
			for(var row = 0; row < gridSize; row++) phi[row * gridSize + col] = colData[row];
		});
		// MathNet's inverse applies 1/N normalization per dimension, so φ is correctly scaled.

		// Interpolate acceleration a = -∇φ back to particles (central differences)
		var longRange = new Vector2D[nBodies];
		for(var idx = 0; idx < nBodies; idx++)
		{
			var body = bodies[idx];
			if(body.IsAbsorbed)
				continue;
			var gx = (body.Position.X - l) / dx;
			var gy = (body.Position.Y - t) / dy;
			var i = (int)Math.Floor(gx);
			var j = (int)Math.Floor(gy);
			if(i < 1 || i >= gridSize - 1 || j < 1 || j >= gridSize - 1)
				continue;
			var dphidx = (phi[j * gridSize + (i + 1)].Real - phi[j * gridSize + (i - 1)].Real) / (2.0 * dx);
			var dphidy = (phi[(j + 1) * gridSize + i].Real - phi[(j - 1) * gridSize + i].Real) / (2.0 * dy);
			longRange[idx] = new Vector2D(-dphidx, -dphidy);
		}

		// Short-range direct correction for neighbors within cutoff
		Parallel.For(0, nBodies, i =>
		{
			var bi = bodies[i];
			if(bi.IsAbsorbed)
				return;
			var acc = longRange[i];
			for(var j = 0; j < nBodies; j++)
			{
				if(i == j) continue;
				var bj = bodies[j];
				if(bj.IsAbsorbed) continue;
				var d = bi.Position - bj.Position;
				var d2 = d.LengthSquared;
				if(d2 < cutoffSq && d2 > Eps)
				{
					var dLen = Math.Sqrt(d2);
					var invd3 = 1.0 / (d2 * dLen);
					acc += -IWorld.G * bj.m * d * invd3;
				}
			}
			bodies[i].a = acc;
		});

		diagnostics.SetField("Strategy", "Particle-Mesh");
		diagnostics.SetField("GridSize", gridSize);
		diagnostics.SetField("dx", dx);
		diagnostics.SetField("dy", dy);
		diagnostics.SetField("Cutoff", cutoffRadius);
	}

	#endregion
}
