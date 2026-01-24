using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal sealed class MinDiameterCrossingTime : Adaptive
{
	#region Construction

	public MinDiameterCrossingTime(int maxSteps,
											  TimeSpan minDt,
											  double safetyFactor)
		: base(maxSteps, minDt, safetyFactor)
	{
	}

	#endregion

	#region Implementation

	/// <inheritdoc />
	protected override TimeSpan AdaptDt(IReadOnlyList<Body> bodiesToProcess, TimeSpan dt)
	{
		// dt bestimmen: kleinstes (Durchmesser / Geschwindigkeit) über alle Entities
		var n = bodiesToProcess.Count;
		var adaptedDtInSeconds = double.PositiveInfinity;

		for(var i = 0; i < n; i++)
		{
			var b = bodiesToProcess[i];

			if(b.IsAbsorbed)
				continue;

			var vlen = b.v.Length;

			if(vlen <= 0.0 ||
			   b.r <= 0.0)
				continue;

			adaptedDtInSeconds = Math.Min(2.0 * b.r / vlen, adaptedDtInSeconds); // Zeit, um eigenen Durchmesser zu überqueren
		}

		return double.IsInfinity(adaptedDtInSeconds) ||
			   adaptedDtInSeconds <= 0.0
				   ? dt // Keine sinnvolle Grenze -> ganze dt nehmen
				   : TimeSpan.FromSeconds(adaptedDtInSeconds);
	}

	#endregion
}