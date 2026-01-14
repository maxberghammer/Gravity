// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class SemiImplicitIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	void IIntegrator.Step(Body[] bodies, double dt, Action<Body[]> computeAccelerations)
	{
		computeAccelerations(bodies);
		var n = bodies.Length;
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * dt;
						   });
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.Position += b.v * dt;
						   });
	}

	#endregion
}