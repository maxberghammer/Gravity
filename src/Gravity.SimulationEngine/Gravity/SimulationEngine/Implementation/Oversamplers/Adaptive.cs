using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal abstract class Adaptive : SimulationEngine.IOversampler
{
	#region Fields

	private readonly int _baseMaxSteps;
	private readonly TimeSpan _minDt;
	private readonly double _safetyFactor;

	#endregion

	#region Construction

	protected Adaptive(int baseMaxSteps, TimeSpan minDt, double safetyFactor)
	{
		_baseMaxSteps = baseMaxSteps;
		_minDt = minDt;
		_safetyFactor = safetyFactor;
	}

	#endregion

	#region Implementation of IOversampler

	/// <inheritdoc/>
	int SimulationEngine.IOversampler.Oversample(IWorld world, IReadOnlyList<Body> bodies, TimeSpan timeSpan, Action<IReadOnlyList<Body>, TimeSpan> processBodies, Diagnostics diagnostics)
	{
		var remaining = timeSpan;
		var steps = 0;

		// Scale maxSteps with Timescale: more time acceleration = more substeps needed
		var maxSteps = (int)Math.Min(_baseMaxSteps * Math.Max(1.0, world.Timescale), 4096);

		while(TimeSpan.Zero < remaining &&
			  steps < maxSteps)
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

	protected abstract TimeSpan AdaptDt(IReadOnlyList<Body> bodiesToProcess, TimeSpan dt);

	#endregion
}