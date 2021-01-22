// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

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