using System;
using System.Threading.Tasks;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal interface ISimulationEngine
	{
		Task SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime);
	}
}