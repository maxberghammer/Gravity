// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal interface IIntegrator
{
	Tuple<int, int>[] Integrate(Body[] entities, TimeSpan deltaTime, Func<Body[], Tuple<int, int>[]> processFunc);
}