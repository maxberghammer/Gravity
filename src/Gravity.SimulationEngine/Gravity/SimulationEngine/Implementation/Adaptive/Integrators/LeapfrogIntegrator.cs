// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Adaptive.Integrators;

internal sealed class LeapfrogIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	void IIntegrator.Step(Entity[] entities, double dt, Action<Entity[]> computeAccelerations)
	{
		var n = entities.Length;
		// a(t)
		computeAccelerations(entities);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var e = entities[i];

							   if(e.IsAbsorbed)
								   return;

							   e.v += e.a * (0.5 * dt);
						   });
		// Drift full step
		Parallel.For(0, n, i =>
						   {
							   var e = entities[i];

							   if(e.IsAbsorbed)
								   return;

							   e.Position += e.v * dt;
						   });
		// a(t+dt)
		computeAccelerations(entities);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var e = entities[i];

							   if(e.IsAbsorbed)
								   return;

							   e.v += e.a * (0.5 * dt);
						   });
	}

	#endregion
}