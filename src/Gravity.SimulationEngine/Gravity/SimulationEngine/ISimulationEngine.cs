// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine;

public interface ISimulationEngine
{
	Task SimulateAsync(Entity[] entities, TimeSpan deltaTime);
}