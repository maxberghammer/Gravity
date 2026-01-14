// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class LeapfrogIntegrator : IIntegrator
{
	#region Fields

	private readonly double _maxDisplacementFactor;
	private readonly int _maxSubsteps;

	#endregion

	#region Construction

	public LeapfrogIntegrator(int maxSubsteps = 8, double maxDisplacementFactor = 0.3)
	{
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSubsteps, 1);
        if (maxDisplacementFactor <= 0 ||
		   maxDisplacementFactor > 1)
			throw new ArgumentOutOfRangeException(nameof(maxDisplacementFactor));

		_maxSubsteps = maxSubsteps;
		_maxDisplacementFactor = maxDisplacementFactor;
	}

	#endregion

	#region Implementation of IIntegrator

	Tuple<int, int>[] IIntegrator.Integrate(Body[] entities, TimeSpan deltaTime, Func<Body[], Tuple<int, int>[]> processFunc)
	{
		var n = entities.Length;

		if(n == 0)
			return Array.Empty<Tuple<int, int>>();

		var dt = deltaTime.TotalSeconds;

		// Determine adaptive substeps by limiting per-substep displacement to a fraction of the smallest radius
		var vMax = 0.0d;
		var rMin = double.MaxValue;

		for(var i = 0; i < n; i++)
		{
			var vLen = entities[i].v.Length;
			if(vLen > vMax)
				vMax = vLen;
			if(entities[i].r < rMin)
				rMin = entities[i].r;
		}

		if(rMin > 1e290)
			rMin = 0.0d;

		var substeps = 1;

		if(vMax > 0.0d &&
		   rMin > 0.0d &&
		   dt > 0.0d)
		{
			var maxDisp = _maxDisplacementFactor * rMin;
			var safeDt = maxDisp / vMax;

			if(safeDt > 0.0d)
			{
				substeps = (int)Math.Ceiling(dt / safeDt);
				substeps = Math.Max(1, Math.Min(substeps, _maxSubsteps));
			}
		}

		var dts = dt / substeps;
		var half = 0.5d * dts;

		// collisions across substeps
		var collisionsSet = new HashSet<long>(256);
		var collisionsAll = new List<Tuple<int, int>>(256);

		for(var s = 0; s < substeps; s++)
		{
			// Ensure accelerations are up-to-date for current positions (pre-kick)
			var c0 = processFunc(entities);
			AddCollisions(collisionsSet, collisionsAll, c0);

			// Kick (half): v += a * (dt/2)
			Parallel.For(0, n, i => { entities[i].v += entities[i].a * half; });

			// Drift: x += v * dt
			Parallel.For(0, n, i => { entities[i].Position += entities[i].v * dts; });

			// Recompute accelerations at new positions
			var c1 = processFunc(entities);
			AddCollisions(collisionsSet, collisionsAll, c1);

			// Kick (half): v += a * (dt/2)
			Parallel.For(0, n, i => { entities[i].v += entities[i].a * half; });
		}

		return collisionsAll.Count == 0
				   ? Array.Empty<Tuple<int, int>>()
				   : collisionsAll.ToArray();
	}

	#endregion

	#region Implementation

	private static void AddCollisions(HashSet<long> set, List<Tuple<int, int>> list, Tuple<int, int>[] src)
	{
		for(var i = 0; i < src.Length; i++)
		{
			var a = src[i].Item1;
			var b = src[i].Item2;
			var min = a < b
						  ? a
						  : b;
			var max = a < b
						  ? b
						  : a;
			var key = ((long)min << 32) | (uint)max;
			if(set.Add(key))
				list.Add(Tuple.Create(min, max));
		}
	}

	#endregion
}