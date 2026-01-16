using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal abstract class Adaptive : SimulationEngine.IOversampler
{
	#region Fields

	private readonly int _maxSteps;
	private readonly TimeSpan _minDt;
	private readonly double _safetyFactor;

	#endregion

	#region Construction

	protected Adaptive(int maxSteps, TimeSpan minDt, double safetyFactor)
	{
		_maxSteps = maxSteps;
		_minDt = minDt;
		_safetyFactor = safetyFactor;
	}

	#endregion

	#region Implementation of IOversampler

	/// <inheritdoc/>
	int SimulationEngine.IOversampler.Oversample(IWorld world, Body[] bodies, TimeSpan timeSpan, Action<Body[], TimeSpan> processBodies, Diagnostics diagnostics)
	{
		var remaining = timeSpan;
		var steps = 0;

		while(TimeSpan.Zero < remaining &&
			  steps < _maxSteps)
		{
			var dt = TimeSpan.Max(_minDt, TimeSpan.Min(remaining, _safetyFactor * AdaptDt(bodies, remaining)));

			processBodies(bodies, dt);

			remaining -= dt;
			steps++;
		}

		return steps;
	}

	#endregion

	#region Implementation

	protected abstract TimeSpan AdaptDt(Body[] bodiesToProcess, TimeSpan dt);

	#endregion
}