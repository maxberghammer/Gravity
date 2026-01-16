// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class SemiImplicit : SimulationEngine.IIntegrator
{
	#region Implementation of IIntegrator

	/// <inheritdoc/>
	void SimulationEngine.IIntegrator.Step(IWorld world, Body[] bodies, double dtInSeconds, Action<Body[]> computation, Diagnostics diagnostics)
	{
		computation(bodies);
		var n = bodies.Length;
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.v += b.a * dtInSeconds;
						   });
		Parallel.For(0, n, i =>
						   {
							   var b = bodies[i];

							   if(b.IsAbsorbed)
								   return;

							   b.Position += b.v * dtInSeconds;
						   });
	}

	#endregion
}