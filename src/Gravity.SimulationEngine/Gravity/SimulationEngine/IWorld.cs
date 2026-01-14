namespace Gravity.SimulationEngine;

public interface IWorld
{
	bool ClosedBoundaries { get; }

	bool ElasticCollisions { get; }

	IViewport Viewport { get; }

	Body[] GetBodies();

	// Correct gravitational constant (SI units)
	public static readonly double G = 6.67430e-11;
}