// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine;

public interface ISimulationEngine
{
	public interface IDiagnostics
	{
		IReadOnlyDictionary<string, object> Fields { get; }
	}

	void Simulate(Entity[] entities, TimeSpan deltaTime);

	IDiagnostics GetDiagnostics();
}