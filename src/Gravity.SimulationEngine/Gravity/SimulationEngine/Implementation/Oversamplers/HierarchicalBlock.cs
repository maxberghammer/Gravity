using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

/// <summary>
/// GADGET-style Hierarchical Block Timestep oversampler.
/// Bodies are organized into discrete "bins" (time levels) where each bin has a power-of-2 timestep.
/// All bodies within a bin are integrated synchronously, avoiding interpolation and state management issues.
/// Key concepts:
/// - Bin 0: Fastest bodies (e.g., Io) with timestep dt_base
/// - Bin 1: Medium bodies with timestep 2 * dt_base
/// - Bin 2: Slow bodies with timestep 4 * dt_base
/// - Bin 3: Slowest bodies with timestep 8 * dt_base
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
/// This ensures regular synchronization points where all bodies are at the same time.
/// </summary>
internal sealed class HierarchicalBlock : SimulationEngine.IOversampler
{
	#region Fields

	private readonly TimeSpan _minDt;
	private readonly int _numBins;
	private readonly double _safetyFactor;
	
	// Cache for body-to-bin assignments to avoid reallocations
	private int[]? _bodyBinCache;
	private Body[][]? _binBodiesCache;

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
	int SimulationEngine.IOversampler.Oversample(IWorld world,
												 IReadOnlyList<Body> bodies,
												 TimeSpan timeSpan,
												 Action<IReadOnlyList<Body>, TimeSpan> processBodies,
												 Diagnostics diagnostics)
	{
		if(bodies.Count == 0)
			return 0;

		// Calculate base timestep (for fastest bin)
		var baseTimestep = CalculateBaseTimestep(bodies, timeSpan);
		baseTimestep = TimeSpan.Max(_minDt, TimeSpan.FromSeconds(_safetyFactor * baseTimestep.TotalSeconds));

		// Assign bodies to bins (reuse cache if possible)
		if(_bodyBinCache == null || _bodyBinCache.Length != bodies.Count)
			_bodyBinCache = new int[bodies.Count];
		
		AssignBodiesToBins(bodies, baseTimestep, _bodyBinCache);

		// Count bodies per bin
		Span<int> binCounts = stackalloc int[_numBins];
		foreach(var bin in _bodyBinCache.AsSpan())
			binCounts[bin]++;

		// Allocate bin arrays (reuse cache if possible)
		if(_binBodiesCache == null || _binBodiesCache.Length != _numBins)
			_binBodiesCache = new Body[_numBins][];
		
		for(var i = 0; i < _numBins; i++)
		{
			if(_binBodiesCache[i] == null || _binBodiesCache[i].Length != binCounts[i])
				_binBodiesCache[i] = new Body[binCounts[i]];
		}

		// Fill bin arrays
		Span<int> binIndices = stackalloc int[_numBins];
		for(var i = 0; i < bodies.Count; i++)
		{
			var bin = _bodyBinCache[i];
			var idx = binIndices[bin]++;
			_binBodiesCache[bin][idx] = bodies[i];
		}

		// Fixed maximum cycles for performance
		const int maxCycles = 128;

		// Calculate minimum baseTimestep to fit timeSpan in maxCycles
		// This ensures we never exceed maxCycles while still covering the full timeSpan
		var minBaseTimestep = timeSpan.TotalSeconds / maxCycles;
		if(baseTimestep.TotalSeconds < minBaseTimestep)
			baseTimestep = TimeSpan.FromSeconds(minBaseTimestep);

		// Now calculate actual cycles needed (will be <= maxCycles)
		var totalCycles = (int)Math.Ceiling(timeSpan.TotalSeconds / baseTimestep.TotalSeconds);

		var steps = 0;
		var baseTimestepSeconds = baseTimestep.TotalSeconds;
		var targetSeconds = timeSpan.TotalSeconds;

		// Execute cycles
		for(var cycle = 0; cycle < totalCycles; cycle++)
		{
			var elapsedTime = cycle * baseTimestepSeconds;
			
			// Early exit if we've used up all time
			if(elapsedTime >= targetSeconds)
				break;
			
			var remaining = targetSeconds - elapsedTime;

			// Determine which bins to integrate in this cycle
			// Bin b integrates every 2^b cycles
			for(var bin = 0; bin < _numBins; bin++)
			{
				var binPeriod = 1 << bin; // 2^bin (power of 2)

				// This bin integrates if cycle is divisible by its period
				if(cycle % binPeriod != 0)
					continue;

				var binBodies = _binBodiesCache[bin];
				if(binBodies.Length == 0)
					continue;

				// Timestep for this bin
				var binTimestepSeconds = Math.Min(baseTimestepSeconds * binPeriod, remaining);
				var binTimestep = TimeSpan.FromSeconds(binTimestepSeconds);

				// Integrate all bodies in this bin
				processBodies(binBodies, binTimestep);
				steps++;
			}
		}

		return steps;
	}

	#endregion

	#region Implementation

	/// <summary>
	/// Calculates the base timestep (for bin 0, the fastest bodies).
	/// </summary>
	private static TimeSpan CalculateBaseTimestep(IReadOnlyList<Body> bodies, TimeSpan maxDt)
	{
		var minCrossingTime = double.PositiveInfinity;

		foreach(var body in bodies)
		{
			var vlen = body.v.Length;

			if(vlen <= 0.0 ||
			   body.r <= 0.0)
				continue;

			var crossingTime = 2.0 * body.r / vlen;
			minCrossingTime = Math.Min(minCrossingTime, crossingTime);
		}

		// If no valid crossing time found (e.g., all bodies stationary), 
		// return a reasonable fraction of maxDt
		if(double.IsInfinity(minCrossingTime) || minCrossingTime <= 0.0)
			return TimeSpan.FromSeconds(maxDt.TotalSeconds / 64.0);

		// Clamp to maxDt - timestep should never exceed the frame time
		return TimeSpan.FromSeconds(Math.Min(minCrossingTime, maxDt.TotalSeconds));
	}

	/// <summary>
	/// Assigns each body to a time bin based on its required timestep.
	/// Directly bins bodies without sorting for maximum performance.
	/// </summary>
	private void AssignBodiesToBins(IReadOnlyList<Body> bodies, TimeSpan baseTimestep, int[] bins)
	{
		var baseTimestepSeconds = baseTimestep.TotalSeconds;
		
		// Directly assign bins based on required timestep vs base timestep
		// No sorting needed - just calculate which power-of-2 bin each body belongs to
		for(var i = 0; i < bodies.Count; i++)
		{
			var body = bodies[i];
			var vlen = body.v.Length;
			
			if(vlen <= 0.0 || body.r <= 0.0)
			{
				// Static bodies go to slowest bin
				bins[i] = _numBins - 1;
				continue;
			}
			
			var requiredDt = 2.0 * body.r / vlen;
			
			// Calculate which bin this body should be in
			// Bin 0: dt <= baseTimestep
			// Bin 1: dt <= 2 * baseTimestep
			// Bin 2: dt <= 4 * baseTimestep
			// etc.
			var ratio = requiredDt / baseTimestepSeconds;
			
			// Use Log2 to find appropriate bin
			var bin = ratio <= 1.0 ? 0 : (int)Math.Floor(Math.Log2(ratio));
			
			// Clamp to valid bin range
			bins[i] = Math.Min(bin, _numBins - 1);
		}
	}

	#endregion
}