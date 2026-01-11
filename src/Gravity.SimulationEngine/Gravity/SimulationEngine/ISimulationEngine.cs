// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;

namespace Gravity.SimulationEngine;

public interface ISimulationEngine
{
	void Simulate(Entity[] entities, TimeSpan deltaTime);
}