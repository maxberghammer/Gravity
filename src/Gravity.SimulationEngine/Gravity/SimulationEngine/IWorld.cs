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

	public static readonly double G = Math.Pow(6.67430d, -11.0);
}