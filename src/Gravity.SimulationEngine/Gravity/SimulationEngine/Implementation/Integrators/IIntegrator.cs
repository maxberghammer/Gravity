// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal interface IIntegrator
{
	Task<Tuple<int, int>[]> IntegrateAsync(Entity[] entities, TimeSpan deltaTime, Func<Entity[], Task<Tuple<int, int>[]>> processFunc);
}