// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Adaptive.Integrators;

internal sealed class SemiImplicitIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	void IIntegrator.Step(Entity[] entities, double dt, Action<Entity[]> computeAccelerations)
	{
		computeAccelerations(entities);
		var n = entities.Length;
		Parallel.For(0, n, i =>
						   {
							   var e = entities[i];

							   if(e.IsAbsorbed)
								   return;

							   e.v += e.a * dt;
						   });
		Parallel.For(0, n, i =>
						   {
							   var e = entities[i];

							   if(e.IsAbsorbed)
								   return;

							   e.Position += e.v * dt;
						   });
	}

	#endregion
}