// Erstellt am: 06.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Threading.Tasks;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine.Integrators
{
	internal interface IIntegrator
	{
		Task<Tuple<int, int>[]> IntegrateAsync(Entity[] aEntities, TimeSpan aDeltaTime, Func<Entity[], Task<Tuple<int, int>[]>> aProcessFunc);
	}
}