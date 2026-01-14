// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Adaptive.Integrators;

internal sealed class WarmStartVerletIntegrator : IIntegrator
{
	#region Fields

	private readonly Dictionary<int, Vector2D> _lastA = new(1024);
	private bool _primed;

	#endregion

	#region Implementation of IIntegrator

	void IIntegrator.Step(Body[] bodies, double dt, Action<Body[]> computeAccelerations)
	{
		var n = bodies.Length;

		if(n == 0)
			return;

		// Prime once to obtain initial accelerations (used as previous a)
		if(!_primed)
		{
			computeAccelerations(bodies);

			for(var i = 0; i < n; i++)
			{
				var b = bodies[i];
				_lastA[b.Id] = b.a;
			}

			_primed = true;
		}

		var aPrev = ArrayPool<Vector2D>.Shared.Rent(n);

		try
		{
			for(var i = 0; i < n; i++)
			{
				var b = bodies[i];
				if(!_lastA.TryGetValue(b.Id, out var ap))
					ap = b.a; // fallback if new entity appears
				aPrev[i] = ap;
			}

			// Half-kick with previous acceleration
			Parallel.For(0, n, i =>
							   {
								   var b = bodies[i];

								   if(b.IsAbsorbed)
									   return;

								   b.v += aPrev[i] * (0.5 * dt);
							   });

			// Drift
			Parallel.For(0, n, i =>
							   {
								   var b = bodies[i];

								   if(b.IsAbsorbed)
									   return;

								   b.Position += b.v * dt;
							   });

			// Compute new accelerations at t+dt
			computeAccelerations(bodies);

			// Half-kick with new acceleration and store for next step
			Parallel.For(0, n, i =>
							   {
								   var b = bodies[i];

								   if(b.IsAbsorbed)
									   return;

								   b.v += b.a * (0.5 * dt);
								   _lastA[b.Id] = b.a;
							   });
		}
		finally
		{
			ArrayPool<Vector2D>.Shared.Return(aPrev);
		}
	}

	#endregion
}