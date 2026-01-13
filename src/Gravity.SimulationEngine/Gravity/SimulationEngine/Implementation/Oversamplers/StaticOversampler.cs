using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal sealed class StaticOversampler : IOversampler
{
	#region Fields

	private readonly int _steps;

	#endregion

	#region Construction

	public StaticOversampler(int steps)
		=> _steps = steps;

	#endregion

	#region Implementation of IOversampler

	int IOversampler.Oversample(Entity[] entitiesToProcess, TimeSpan timeSpan, Action<Entity[], TimeSpan> processEntities)
	{
		var dt = timeSpan / _steps;

		for(var i = 0; i < _steps; i++)
			processEntities(entitiesToProcess, dt);

		return _steps;
	}

	#endregion
}