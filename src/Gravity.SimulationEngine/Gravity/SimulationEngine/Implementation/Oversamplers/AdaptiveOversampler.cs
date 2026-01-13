using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal abstract class AdaptiveOversampler : IOversampler
{
	#region Fields

	private readonly int _maxSteps;
	private readonly TimeSpan _minDt;
	private readonly double _safetyFactor;

	#endregion

	#region Construction

	protected AdaptiveOversampler(int maxSteps, TimeSpan minDt, double safetyFactor)
	{
		_maxSteps = maxSteps;
		_minDt = minDt;
		_safetyFactor = safetyFactor;
	}

	#endregion

	#region Implementation of IOversampler

	int IOversampler.Oversample(Entity[] entitiesToProcess, TimeSpan timeSpan, Action<Entity[], TimeSpan> processEntities)
	{
		var remaining = timeSpan;
		var steps = 0;

		while(TimeSpan.Zero < remaining &&
			  steps < _maxSteps)
		{
			var dt = TimeSpan.Max(_minDt, TimeSpan.Min(remaining, _safetyFactor * AdaptDt(entitiesToProcess, remaining)));

			processEntities(entitiesToProcess, dt);

			remaining -= dt;
			steps++;
		}

		return steps;
	}

	#endregion

	#region Implementation

	protected abstract TimeSpan AdaptDt(Entity[] entitiesToProcess, TimeSpan dt);

	#endregion
}