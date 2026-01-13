using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal sealed class MinDiameterCrossingTimeOversampler : AdaptiveOversampler
{
	#region Construction

	public MinDiameterCrossingTimeOversampler(int maxSteps,
											  TimeSpan minDt,
											  double safetyFactor)
		: base(maxSteps, minDt, safetyFactor)
	{
	}

	#endregion

	#region Implementation

	protected override TimeSpan AdaptDt(Entity[] entitiesToProcess, TimeSpan dt)
	{
		// dt bestimmen: kleinstes (Durchmesser / Geschwindigkeit) über alle Entities
		var n = entitiesToProcess.Length;
		var adaptedDtInSeconds = dt.TotalSeconds;

		for(var i = 0; i < n; i++)
		{
			var e = entitiesToProcess[i];

			if(e.IsAbsorbed)
				continue;

			var vlen = e.v.Length;

			if(vlen <= double.Epsilon ||
			   e.r <= double.Epsilon)
				continue;

			adaptedDtInSeconds = Math.Min(2.0 * e.r / vlen, adaptedDtInSeconds); // Zeit, um eigenen Durchmesser zu überqueren

			if(adaptedDtInSeconds <= double.Epsilon)
				break;
		}

		return adaptedDtInSeconds <= double.Epsilon
				   ? dt
				   : TimeSpan.FromSeconds(adaptedDtInSeconds);
	}

	#endregion
}