namespace Gravity.SimulationEngine;

public interface IWorld
{
	bool ClosedBoundaries { get; }

	bool ElasticCollisions { get; }

	/// <summary>
	/// The time scale factor applied to the simulation (1.0 = realtime).
	/// </summary>
	double TimeScaleFactor { get; }

	IViewport Viewport { get; }

	Body[] GetBodies();

	// Correct gravitational constant (SI units)
	public static readonly double G = 6.67430e-11;
}