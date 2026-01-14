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

	protected override TimeSpan AdaptDt(Body[] entitiesToProcess, TimeSpan dt)
	{
		// dt bestimmen: kleinstes (Durchmesser / Geschwindigkeit) über alle Entities
		var n = entitiesToProcess.Length;
		var adaptedDtInSeconds = double.PositiveInfinity;

		for(var i = 0; i < n; i++)
		{
			var e = entitiesToProcess[i];

			if(e.IsAbsorbed)
				continue;

			var vlen = e.v.Length;

			if(vlen <= 0.0 ||
			   e.r <= 0.0)
				continue;

			adaptedDtInSeconds = Math.Min(2.0 * e.r / vlen, adaptedDtInSeconds); // Zeit, um eigenen Durchmesser zu überqueren
		}

		return double.IsInfinity(adaptedDtInSeconds) ||
			   adaptedDtInSeconds <= 0.0
				   ? dt // Keine sinnvolle Grenze -> ganze dt nehmen
				   : TimeSpan.FromSeconds(adaptedDtInSeconds);
	}

	#endregion
}