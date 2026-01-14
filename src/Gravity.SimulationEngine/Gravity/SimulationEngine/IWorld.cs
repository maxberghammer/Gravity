using System;
using System.Collections.Generic;

namespace Gravity.SimulationEngine;

public interface IWorld
{
	public static int GetPreferredChunkSize<T>(IReadOnlyCollection<T> collection)
		=> null == collection
			   ? throw new ArgumentNullException(nameof(collection))
			   : collection.Count / Environment.ProcessorCount;

	bool ClosedBoundaries { get; }

	bool ElasticCollisions { get; }

	IViewport Viewport { get; }

	Body[] GetBodies();

	// Correct gravitational constant (SI units)
	public static readonly double G = 6.67430e-11;
}