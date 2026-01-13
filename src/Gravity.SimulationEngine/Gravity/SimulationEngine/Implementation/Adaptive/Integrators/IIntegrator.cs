// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;

namespace Gravity.SimulationEngine.Implementation.Adaptive.Integrators;

internal interface IIntegrator
{
	void Step(Entity[] entities, double dt, Action<Entity[]> computeAccelerations);
}