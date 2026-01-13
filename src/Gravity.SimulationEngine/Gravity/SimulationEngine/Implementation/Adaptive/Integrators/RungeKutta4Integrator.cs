// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Adaptive.Integrators;

internal sealed class RungeKutta4Integrator : IIntegrator
{
	#region Implementation of IIntegrator

	void IIntegrator.Step(Entity[] entities, double dt, Action<Entity[]> computeAccelerations)
	{
		var n = entities.Length;

		if(n == 0)
			return;

		var pos0 = ArrayPool<Vector2D>.Shared.Rent(n);
		var vel0 = ArrayPool<Vector2D>.Shared.Rent(n);
		// ReSharper disable InconsistentNaming
		var k1x = ArrayPool<Vector2D>.Shared.Rent(n);
		var k2x = ArrayPool<Vector2D>.Shared.Rent(n);
		var k3x = ArrayPool<Vector2D>.Shared.Rent(n);
		var k4x = ArrayPool<Vector2D>.Shared.Rent(n);
		var k1v = ArrayPool<Vector2D>.Shared.Rent(n);
		var k2v = ArrayPool<Vector2D>.Shared.Rent(n);
		var k3v = ArrayPool<Vector2D>.Shared.Rent(n);
		var k4v = ArrayPool<Vector2D>.Shared.Rent(n);
		// ReSharper enable InconsistentNaming
		var dtHalf = dt * 0.5;

		try
		{
			// Snapshot initial state
			Parallel.For(0, n, i =>
							   {
								   pos0[i] = entities[i].Position;
								   vel0[i] = entities[i].v;
							   });

			// k1
			computeAccelerations(entities); // a(x0)
			Parallel.For(0, n, i =>
							   {
								   k1x[i] = vel0[i];
								   k1v[i] = entities[i].a;
							   });

			// k2: at x0 + dt/2 * k1x
			Parallel.For(0, n, i => { entities[i].Position = pos0[i] + dtHalf * k1x[i]; });
			computeAccelerations(entities); // a(x0 + dt/2*k1x)
			Parallel.For(0, n, i =>
							   {
								   k2x[i] = vel0[i] + dtHalf * k1v[i];
								   k2v[i] = entities[i].a;
							   });

			// k3: at x0 + dt/2 * k2x
			Parallel.For(0, n, i => { entities[i].Position = pos0[i] + dtHalf * k2x[i]; });
			computeAccelerations(entities); // a(x0 + dt/2*k2x)
			Parallel.For(0, n, i =>
							   {
								   k3x[i] = vel0[i] + dtHalf * k2v[i];
								   k3v[i] = entities[i].a;
							   });

			// k4: at x0 + dt * k3x
			Parallel.For(0, n, i => { entities[i].Position = pos0[i] + dt * k3x[i]; });
			computeAccelerations(entities); // a(x0 + dt*k3x)
			Parallel.For(0, n, i =>
							   {
								   k4x[i] = vel0[i] + dt * k3v[i];
								   k4v[i] = entities[i].a;
							   });

			// Combine increments
			Parallel.For(0, n, i =>
							   {
								   if(entities[i].IsAbsorbed)
									   return;

								   var dx = dt / 6.0 * (k1x[i] + 2.0 * (k2x[i] + k3x[i]) + k4x[i]);
								   var dv = dt / 6.0 * (k1v[i] + 2.0 * (k2v[i] + k3v[i]) + k4v[i]);
								   entities[i].Position = pos0[i] + dx;
								   entities[i].v = vel0[i] + dv;
							   });
		}
		finally
		{
			ArrayPool<Vector2D>.Shared.Return(pos0);
			ArrayPool<Vector2D>.Shared.Return(vel0);
			ArrayPool<Vector2D>.Shared.Return(k1x);
			ArrayPool<Vector2D>.Shared.Return(k2x);
			ArrayPool<Vector2D>.Shared.Return(k3x);
			ArrayPool<Vector2D>.Shared.Return(k4x);
			ArrayPool<Vector2D>.Shared.Return(k1v);
			ArrayPool<Vector2D>.Shared.Return(k2v);
			ArrayPool<Vector2D>.Shared.Return(k3v);
			ArrayPool<Vector2D>.Shared.Return(k4v);
		}
	}

	#endregion
}