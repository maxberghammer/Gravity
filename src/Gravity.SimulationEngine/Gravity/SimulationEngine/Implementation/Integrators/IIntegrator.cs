// Erstellt am: 13.01.2026
// Erstellt von: MaxBerghammer

using System;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal interface IIntegrator
{
	void Step(Body[] bodies, double dt, Action<Body[]> computeAccelerations);
}