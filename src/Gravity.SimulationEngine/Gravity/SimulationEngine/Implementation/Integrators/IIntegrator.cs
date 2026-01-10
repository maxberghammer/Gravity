// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal interface IIntegrator
{
	Tuple<int, int>[] Integrate(Entity[] entities, TimeSpan deltaTime, Func<Entity[], Tuple<int, int>[]> processFunc);
}