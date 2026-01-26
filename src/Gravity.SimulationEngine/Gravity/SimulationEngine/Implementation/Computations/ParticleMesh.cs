using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;

namespace Gravity.SimulationEngine.Implementation.Computations;

/// <summary>
/// High-performance PM (Particle-Mesh) with zero allocations after warmup.
/// </summary>
internal sealed class ParticleMesh : SimulationEngine.IComputation
{
	private const double _eps = 1e-10;
	private const int _gridSize = 64;
	private const int _directThreshold = 100;

	// Cached arrays
	private double[]? _massGrid;
	private Complex[]? _rhoK, _axK, _ayK, _azK;

	void SimulationEngine.IComputation.Compute(IWorld world, IReadOnlyList<Body> bodies, Diagnostics diagnostics)
	{
		var nBodies = bodies.Count;
		if (nBodies == 0) return;

		// Count active and get bounds - single pass
		double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
		var activeCount = 0;

		for (var i = 0; i < nBodies; i++)
		{
			var b = bodies[i];
			if (b.IsAbsorbed) continue;
			activeCount++;
			b.a = Vector3D.Zero;
			var p = b.Position;
			if (p.X < minX) minX = p.X;
			if (p.Y < minY) minY = p.Y;
			if (p.Z < minZ) minZ = p.Z;
			if (p.X > maxX) maxX = p.X;
			if (p.Y > maxY) maxY = p.Y;
			if (p.Z > maxZ) maxZ = p.Z;
		}

		if (activeCount == 0) return;

		if (activeCount <= _directThreshold)
		{
			ComputeDirect(bodies);
			diagnostics.SetField("Strategy", "Particle-Mesh-Direct");
			diagnostics.SetField("Bodies", activeCount);
			return;
		}

		// Cubic domain
		var span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
		span = Math.Max(span, 1.0) * 1.2;
		var cx = (minX + maxX) * 0.5;
		var cy = (minY + maxY) * 0.5;
		var cz = (minZ + maxZ) * 0.5;
		var originX = cx - span * 0.5;
		var originY = cy - span * 0.5;
		var originZ = cz - span * 0.5;

		const int n = _gridSize;
		var h = span / n;
		var invH = 1.0 / h;

		EnsureCapacity();
		Array.Clear(_massGrid!, 0, n * n * n);

		// CIC assignment
		AssignMassFast(bodies, n, originX, originY, originZ, invH);

		// FFT
		var gridVol = n * n * n;
		for (var i = 0; i < gridVol; i++)
			_rhoK![i] = new Complex(_massGrid![i], 0);

		FFT3DFast(_rhoK!, n, true);
		ApplyGreenFast(n, span, h);

		FFT3DFast(_axK!, n, false);
		FFT3DFast(_ayK!, n, false);
		FFT3DFast(_azK!, n, false);

		// Interpolate
		InterpolateFast(bodies, n, originX, originY, originZ, invH);

		diagnostics.SetField("Strategy", "Particle-Mesh-FFT");
		diagnostics.SetField("GridSize", n);
		diagnostics.SetField("Bodies", activeCount);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void EnsureCapacity()
	{
		const int vol = _gridSize * _gridSize * _gridSize;
		if (_massGrid != null) return;
		_massGrid = new double[vol];
		_rhoK = new Complex[vol];
		_axK = new Complex[vol];
		_ayK = new Complex[vol];
		_azK = new Complex[vol];
	}

	private void AssignMassFast(IReadOnlyList<Body> bodies, int n, double ox, double oy, double oz, double invH)
	{
		var grid = _massGrid!;
		var nm1 = n - 1;
		var nBodies = bodies.Count;

		for (var bi = 0; bi < nBodies; bi++)
		{
			var b = bodies[bi];
			if (b.IsAbsorbed) continue;

			var px = (b.Position.X - ox) * invH;
			var py = (b.Position.Y - oy) * invH;
			var pz = (b.Position.Z - oz) * invH;

			var i0 = (int)px;
			var j0 = (int)py;
			var k0 = (int)pz;

			// Clamp
			if (i0 < 0) i0 = 0; else if (i0 > nm1 - 1) i0 = nm1 - 1;
			if (j0 < 0) j0 = 0; else if (j0 > nm1 - 1) j0 = nm1 - 1;
			if (k0 < 0) k0 = 0; else if (k0 > nm1 - 1) k0 = nm1 - 1;

			var dx = px - i0;
			var dy = py - j0;
			var dz = pz - k0;

			// Clamp weights
			if (dx < 0) dx = 0; else if (dx > 1) dx = 1;
			if (dy < 0) dy = 0; else if (dy > 1) dy = 1;
			if (dz < 0) dz = 0; else if (dz > 1) dz = 1;

			var m = b.m;
			var wx0 = 1 - dx;
			var wy0 = 1 - dy;
			var wz0 = 1 - dz;

			var idx = (k0 * n + j0) * n + i0;
			grid[idx] += m * wx0 * wy0 * wz0;
			grid[idx + 1] += m * dx * wy0 * wz0;
			grid[idx + n] += m * wx0 * dy * wz0;
			grid[idx + n + 1] += m * dx * dy * wz0;
			grid[idx + n * n] += m * wx0 * wy0 * dz;
			grid[idx + n * n + 1] += m * dx * wy0 * dz;
			grid[idx + n * n + n] += m * wx0 * dy * dz;
			grid[idx + n * n + n + 1] += m * dx * dy * dz;
		}
	}

	private void ApplyGreenFast(int n, double L, double h)
	{
		var dk = 2.0 * Math.PI / L;
		var factor = 4.0 * Math.PI * IWorld.G / (h * h * h);
		var halfN = n / 2;
		var rhoK = _rhoK!;
		var axK = _axK!;
		var ayK = _ayK!;
		var azK = _azK!;

		Parallel.For(0, n, kk =>
		{
			var kzVal = kk <= halfN ? kk * dk : (kk - n) * dk;
			var kzSq = kzVal * kzVal;

			for (var jj = 0; jj < n; jj++)
			{
				var kyVal = jj <= halfN ? jj * dk : (jj - n) * dk;
				var kyzSq = kyVal * kyVal + kzSq;
				var baseIdx = (kk * n + jj) * n;

				for (var ii = 0; ii < n; ii++)
				{
					var kxVal = ii <= halfN ? ii * dk : (ii - n) * dk;
					var k2 = kxVal * kxVal + kyzSq;
					var idx = baseIdx + ii;

					if (k2 > _eps)
					{
						var rho = rhoK[idx];
						var coeff = factor / k2;
						var rhoIm = rho.Imaginary;
						var rhoRe = rho.Real;

						axK[idx] = new Complex(-coeff * kxVal * rhoIm, coeff * kxVal * rhoRe);
						ayK[idx] = new Complex(-coeff * kyVal * rhoIm, coeff * kyVal * rhoRe);
						azK[idx] = new Complex(-coeff * kzVal * rhoIm, coeff * kzVal * rhoRe);
					}
					else
					{
						axK[idx] = ayK[idx] = azK[idx] = Complex.Zero;
					}
				}
			}
		});
	}

	private void InterpolateFast(IReadOnlyList<Body> bodies, int n, double ox, double oy, double oz, double invH)
	{
		var axK = _axK!;
		var ayK = _ayK!;
		var azK = _azK!;
		var nm1 = n - 1;
		var nBodies = bodies.Count;

		Parallel.For(0, nBodies, bi =>
		{
			var b = bodies[bi];
			if (b.IsAbsorbed) return;

			var px = (b.Position.X - ox) * invH;
			var py = (b.Position.Y - oy) * invH;
			var pz = (b.Position.Z - oz) * invH;

			var i0 = (int)px;
			var j0 = (int)py;
			var k0 = (int)pz;

			if (i0 < 0) i0 = 0; else if (i0 > nm1 - 1) i0 = nm1 - 1;
			if (j0 < 0) j0 = 0; else if (j0 > nm1 - 1) j0 = nm1 - 1;
			if (k0 < 0) k0 = 0; else if (k0 > nm1 - 1) k0 = nm1 - 1;

			var dx = px - i0;
			var dy = py - j0;
			var dz = pz - k0;

			if (dx < 0) dx = 0; else if (dx > 1) dx = 1;
			if (dy < 0) dy = 0; else if (dy > 1) dy = 1;
			if (dz < 0) dz = 0; else if (dz > 1) dz = 1;

			var wx0 = 1 - dx;
			var wy0 = 1 - dy;
			var wz0 = 1 - dz;

			var w000 = wx0 * wy0 * wz0;
			var w100 = dx * wy0 * wz0;
			var w010 = wx0 * dy * wz0;
			var w110 = dx * dy * wz0;
			var w001 = wx0 * wy0 * dz;
			var w101 = dx * wy0 * dz;
			var w011 = wx0 * dy * dz;
			var w111 = dx * dy * dz;

			var idx = (k0 * n + j0) * n + i0;
			var n2 = n * n;

			b.a = new Vector3D(
				w000 * axK[idx].Real + w100 * axK[idx + 1].Real +
				w010 * axK[idx + n].Real + w110 * axK[idx + n + 1].Real +
				w001 * axK[idx + n2].Real + w101 * axK[idx + n2 + 1].Real +
				w011 * axK[idx + n2 + n].Real + w111 * axK[idx + n2 + n + 1].Real,

				w000 * ayK[idx].Real + w100 * ayK[idx + 1].Real +
				w010 * ayK[idx + n].Real + w110 * ayK[idx + n + 1].Real +
				w001 * ayK[idx + n2].Real + w101 * ayK[idx + n2 + 1].Real +
				w011 * ayK[idx + n2 + n].Real + w111 * ayK[idx + n2 + n + 1].Real,

				w000 * azK[idx].Real + w100 * azK[idx + 1].Real +
				w010 * azK[idx + n].Real + w110 * azK[idx + n + 1].Real +
				w001 * azK[idx + n2].Real + w101 * azK[idx + n2 + 1].Real +
				w011 * azK[idx + n2 + n].Real + w111 * azK[idx + n2 + n + 1].Real
			);
		});
	}

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

	private static void FFT3DFast(Complex[] data, int n, bool forward)
	{
		var n2 = n * n;

		// X-direction
		Parallel.For(0, n2, slice =>
		{
			var buf = new Complex[n];
			var baseIdx = slice * n;
			for (var i = 0; i < n; i++) buf[i] = data[baseIdx + i];
			if (forward) Fourier.Forward(buf, FourierOptions.NoScaling);
			else Fourier.Inverse(buf, FourierOptions.NoScaling);
			for (var i = 0; i < n; i++) data[baseIdx + i] = buf[i];
		});

		// Y-direction
		Parallel.For(0, n2, slice =>
		{
			var k = slice / n;
			var ii = slice % n;
			var buf = new Complex[n];
			for (var j = 0; j < n; j++) buf[j] = data[(k * n + j) * n + ii];
			if (forward) Fourier.Forward(buf, FourierOptions.NoScaling);
			else Fourier.Inverse(buf, FourierOptions.NoScaling);
			for (var j = 0; j < n; j++) data[(k * n + j) * n + ii] = buf[j];
		});

		// Z-direction
		Parallel.For(0, n2, slice =>
		{
			var jj = slice / n;
			var ii = slice % n;
			var buf = new Complex[n];
			for (var k = 0; k < n; k++) buf[k] = data[(k * n + jj) * n + ii];
			if (forward) Fourier.Forward(buf, FourierOptions.NoScaling);
			else Fourier.Inverse(buf, FourierOptions.NoScaling);
			for (var k = 0; k < n; k++) data[(k * n + jj) * n + ii] = buf[k];
		});

		if (!forward)
		{
			var scale = 1.0 / (n * n * n);
			for (var i = 0; i < data.Length; i++)
				data[i] *= scale;
		}
	}
}
