// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class Leapfrog : SimulationEngine.IIntegrator
{
	#region Implementation of IIntegrator

	/// <inheritdoc/>
	void SimulationEngine.IIntegrator.Step(IWorld world, IReadOnlyList<Body> bodies, double dtInSeconds, Action<IReadOnlyList<Body>> computation, Diagnostics diagnostics)
	{
		var n = bodies.Count;
		// a(t)
		computation(bodies);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   // kann das sein?
							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * (0.5 * dtInSeconds);
						   });
		// Drift full step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.Position += b.v * dtInSeconds;
						   });
		// a(t+dt)
		computation(bodies);
		// Kick half step
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * (0.5 * dtInSeconds);
						   });
	}

	#endregion
}