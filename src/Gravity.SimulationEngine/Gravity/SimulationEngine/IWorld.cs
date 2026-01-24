using System.Collections.Generic;

namespace Gravity.SimulationEngine;

public interface IWorld
{
	bool ClosedBoundaries { get; }

	bool ElasticCollisions { get; }

	/// <summary>
	/// The time scale applied to the simulation (1.0 = realtime).
	/// </summary>
	double Timescale { get; }

	IReadOnlyList<Body> GetBodies();

	void RemoveBodies(IReadOnlyCollection<Body> bodies);

	// Correct gravitational constant (SI units)
	public static readonly double G = 6.67430e-11;
}