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

	void IIntegrator.Step(Entity[] entities, double dt, Action<Entity[]> computeAccelerations)
	{
		var n = entities.Length;

		if(n == 0)
			return;

		// Prime once to obtain initial accelerations (used as previous a)
		if(!_primed)
		{
			computeAccelerations(entities);

			for(var i = 0; i < n; i++)
			{
				var e = entities[i];
				_lastA[e.Id] = e.a;
			}

			_primed = true;
		}

		var aPrev = ArrayPool<Vector2D>.Shared.Rent(n);

		try
		{
			for(var i = 0; i < n; i++)
			{
				var e = entities[i];
				if(!_lastA.TryGetValue(e.Id, out var ap))
					ap = e.a; // fallback if new entity appears
				aPrev[i] = ap;
			}

			// Half-kick with previous acceleration
			Parallel.For(0, n, i =>
							   {
								   var e = entities[i];

								   if(e.IsAbsorbed)
									   return;

								   e.v += aPrev[i] * (0.5 * dt);
							   });

			// Drift
			Parallel.For(0, n, i =>
							   {
								   var e = entities[i];

								   if(e.IsAbsorbed)
									   return;

								   e.Position += e.v * dt;
							   });

			// Compute new accelerations at t+dt
			computeAccelerations(entities);

			// Half-kick with new acceleration and store for next step
			Parallel.For(0, n, i =>
							   {
								   var e = entities[i];

								   if(e.IsAbsorbed)
									   return;

								   e.v += e.a * (0.5 * dt);
								   _lastA[e.Id] = e.a;
							   });
		}
		finally
		{
			ArrayPool<Vector2D>.Shared.Return(aPrev);
		}
	}

	#endregion
}