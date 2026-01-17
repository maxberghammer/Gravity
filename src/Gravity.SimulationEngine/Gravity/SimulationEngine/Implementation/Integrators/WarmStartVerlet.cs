// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class WarmStartVerlet : SimulationEngine.IIntegrator
{
	#region Fields

	private readonly Dictionary<int, Vector3D> _lastA = new(1024);
	private bool _primed;

	#endregion

	#region Implementation of IIntegrator

	/// <inheritdoc/>
	void SimulationEngine.IIntegrator.Step(IWorld world, Body[] bodies, double dtInSeconds, Action<Body[]> computation, Diagnostics diagnostics)
	{
		var n = bodies.Length;

		if(n == 0)
			return;

		// Prime once to obtain initial accelerations (used as previous a)
		if(!_primed)
		{
			computation(bodies);

			for(var i = 0; i < n; i++)
			{
				var b = bodies[i];
				_lastA[b.Id] = b.a;
			}

			_primed = true;
		}

		var aPrev = ArrayPool<Vector3D>.Shared.Rent(n);

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

								   b.v += aPrev[i] * (0.5 * dtInSeconds);
							   });

			// Drift
			Parallel.For(0, n, i =>
							   {
								   var b = bodies[i];

								   if(b.IsAbsorbed)
									   return;

								   b.Position += b.v * dtInSeconds;
							   });

			// Compute new accelerations at t+dt
			computation(bodies);

			// Half-kick with new acceleration and store for next step
			Parallel.For(0, n, i =>
							   {
								   var b = bodies[i];

								   if(b.IsAbsorbed)
									   return;

								   b.v += b.a * (0.5 * dtInSeconds);
								   _lastA[b.Id] = b.a;
							   });
		}
		finally
		{
			ArrayPool<Vector3D>.Shared.Return(aPrev);
		}
	}

	#endregion
}