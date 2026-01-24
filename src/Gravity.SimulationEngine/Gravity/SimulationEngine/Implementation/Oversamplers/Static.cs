using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal class Static : SimulationEngine.IOversampler
{
	#region Fields

	private readonly int _steps;

	#endregion

	#region Construction

	public Static(int steps)
		=> _steps = steps;

	#endregion

	#region Implementation of IOversampler

	/// <inheritdoc/>
	int SimulationEngine.IOversampler.Oversample(IWorld world, IReadOnlyList<Body> bodies, TimeSpan timeSpan, Action<IReadOnlyList<Body>, TimeSpan> processBodies, Diagnostics diagnostics)
	{
		var dt = timeSpan / _steps;

		for(var i = 0; i < _steps; i++)
			processBodies(bodies, dt);

		return _steps;
	}

	#endregion
}