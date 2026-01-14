// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class LeapfrogIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	void IIntegrator.Step(Body[] bodies, double dt, Action<Body[]> computeAccelerations)
	{
		var n = bodies.Length;
		// a(t)
		computeAccelerations(bodies);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];


							   // kann das sein?
							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * (0.5 * dt);
						   });
		// Drift full step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.Position += b.v * dt;
						   });
		// a(t+dt)
		computeAccelerations(bodies);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * (0.5 * dt);
						   });
	}

	#endregion
}