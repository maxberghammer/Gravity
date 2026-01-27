using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

/// <summary>
/// GADGET-style Hierarchical Block Timestep oversampler.
/// 
/// Bodies are organized into discrete "bins" (time levels) where each bin has a power-of-2 timestep.
/// All bodies within a bin are integrated synchronously, avoiding interpolation and state management issues.
/// 
/// Key concepts:
/// - Bin 0: Fastest bodies (e.g., Io) with timestep dt_base
/// - Bin 1: Medium bodies with timestep 2 * dt_base
/// - Bin 2: Slow bodies with timestep 4 * dt_base
/// - Bin 3: Slowest bodies with timestep 8 * dt_base
/// 
/// Integration schedule (cycle-based):
/// Cycle 0: Integrate Bins 0, 1, 2, 3 (all bins)
/// Cycle 1: Integrate Bin 0 only
/// Cycle 2: Integrate Bins 0, 1
/// Cycle 3: Integrate Bin 0 only
/// Cycle 4: Integrate Bins 0, 1, 2
/// Cycle 5: Integrate Bin 0 only
/// Cycle 6: Integrate Bins 0, 1
/// Cycle 7: Integrate Bin 0 only
/// Cycle 8: Integrate Bins 0, 1, 2, 3 (full sync)
/// 
/// This ensures regular synchronization points where all bodies are at the same time.
/// </summary>
internal sealed class HierarchicalBlock : SimulationEngine.IOversampler
{
	#region Fields

	private readonly int _numBins;
	private readonly TimeSpan _minDt;
	private readonly double _safetyFactor;

	#endregion

	#region Construction

	/// <summary>
	/// Creates a new hierarchical block timestep oversampler.
	/// </summary>
	/// <param name="numBins">Number of time bins (default 4: bins 0-3)</param>
	/// <param name="minDt">Minimum timestep for fastest bin</param>
	/// <param name="safetyFactor">Safety factor for timestep calculation (default 0.5)</param>
	public HierarchicalBlock(int numBins = 4,
	                         TimeSpan? minDt = null,
	                         double safetyFactor = 0.5)
	{
		_numBins = numBins;
		_minDt = minDt ?? TimeSpan.FromSeconds(1e-7);
		_safetyFactor = safetyFactor;
	}

	#endregion

	#region Implementation of IOversampler

	/// <inheritdoc/>
	int SimulationEngine.IOversampler.Oversample(IWorld world, IReadOnlyList<Body> bodies, TimeSpan timeSpan, Action<IReadOnlyList<Body>, TimeSpan> processBodies, Diagnostics diagnostics)
	{
		if (bodies.Count == 0)
			return 0;

		// Calculate base timestep (for fastest bin)
		var baseTimestep = CalculateBaseTimestep(bodies, timeSpan);
		baseTimestep = TimeSpan.Max(_minDt, TimeSpan.FromSeconds(_safetyFactor * baseTimestep.TotalSeconds));

		// Assign bodies to bins
		var bodyBins = AssignBodiesToBins(bodies, baseTimestep);

		// Create bins with their respective bodies
		var bins = new List<Body>[_numBins];
		for (var i = 0; i < _numBins; i++)
		{
			bins[i] = new List<Body>();
		}
		
		for (var i = 0; i < bodies.Count; i++)
		{
			var bin = bodyBins[i];
			bins[bin].Add(bodies[i]);
		}

		// Calculate how many cycles we need to cover the timeSpan
		// One cycle = one base timestep
		var totalCycles = (int)Math.Ceiling(timeSpan.TotalSeconds / baseTimestep.TotalSeconds);
		var steps = 0;

		// Execute cycles
		for (var cycle = 0; cycle < totalCycles; cycle++)
		{
			// Determine which bins to integrate in this cycle
			// Bin b integrates every 2^b cycles
			for (var bin = 0; bin < _numBins; bin++)
			{
				var binPeriod = 1 << bin; // 2^bin (power of 2)
				
				// This bin integrates if cycle is divisible by its period
				if (cycle % binPeriod == 0)
				{
					var binBodies = bins[bin];
					
					if (binBodies.Count > 0)
					{
						// Timestep for this bin
						// Timestep for this bin
						var binTimestepSeconds = baseTimestep.TotalSeconds * binPeriod;

						// Clamp to remaining time in frame (to avoid overshooting)
						var elapsedTime = cycle * baseTimestep.TotalSeconds;
						var remaining = timeSpan.TotalSeconds - elapsedTime;
						binTimestepSeconds = Math.Min(binTimestepSeconds, remaining);

						var binTimestep = TimeSpan.FromSeconds(binTimestepSeconds);

						// Integrate all bodies in this bin
                        processBodies(binBodies, binTimestep);
						steps++;
					}
				}
			}
		}

		return steps;
	}

    #endregion

    #region Implementation

    /// <summary>
    /// Assigns each body to a time bin based on its required timestep.
    /// Returns array where index is body index, value is bin number (0 = fastest).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1172:Unused method parameters should be removed", Justification = "<Pending>")]
	private int[] AssignBodiesToBins(IReadOnlyList<Body> bodies, TimeSpan baseTimestep)
	{
		var bins = new int[bodies.Count];

		// If fewer bodies than bins, put all in Bin 0
		if (bodies.Count < _numBins)
			return bins; // Already initialized to 0

		// Calculate required timestep for each body
		var bodyTimesteps = new List<(int index, double requiredDt)>();
		
		for (var i = 0; i < bodies.Count; i++)
		{
			var body = bodies[i];
			var vlen = body.v.Length;
			var requiredDt = vlen > 0.0 && body.r > 0.0
								 ? 2.0 * body.r / vlen
								 : double.MaxValue;

			bodyTimesteps.Add((i, requiredDt));
		}

		// Sort by required dt (smallest first = fastest = Bin 0)
		bodyTimesteps.Sort((a, b) => a.requiredDt.CompareTo(b.requiredDt));

		// Distribute bodies evenly across bins using quantiles
		// This ensures similar-speed bodies are in the same bin
		var bodiesPerBin = bodies.Count / _numBins;

		for(var i = 0; i < bodyTimesteps.Count; i++)
		{
			var bodyInfo = bodyTimesteps[i];
			var bin = Math.Min(_numBins - 1, i / bodiesPerBin);
			bins[bodyInfo.index] = bin;
		}

		return bins;
	}

	/// <summary>
	/// Calculates the base timestep (for bin 0, the fastest bodies).
	/// </summary>
	private static TimeSpan CalculateBaseTimestep(IReadOnlyList<Body> bodies, TimeSpan maxDt)
	{
		var minCrossingTime = double.PositiveInfinity;

		foreach (var body in bodies)
		{
			var vlen = body.v.Length;
			if (vlen <= 0.0 || body.r <= 0.0)
				continue;

			var crossingTime = 2.0 * body.r / vlen;
			minCrossingTime = Math.Min(minCrossingTime, crossingTime);
		}

		// If no valid crossing time found (e.g., all bodies stationary), 
		// return a reasonable fraction of maxDt instead of the full maxDt
		return double.IsInfinity(minCrossingTime) || minCrossingTime <= 0.0
			? TimeSpan.FromSeconds(maxDt.TotalSeconds / 64.0) // Same as MinDiameterCrossingTime's maxSteps default
			: TimeSpan.FromSeconds(minCrossingTime);
	}

	#endregion
}
