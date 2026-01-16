namespace Gravity.SimulationEngine.Implementation.Adaptive;

/// <summary>
/// Strategy interface for computing gravitational accelerations in adaptive N-body simulation
/// Allows pluggable algorithms (Barnes-Hut, Particle-Mesh, etc.)
/// </summary>
internal interface IAccelerationStrategy
{
	/// <summary>
	/// Compute gravitational accelerations for all bodies and store in body.a
	/// </summary>
	void ComputeAccelerations(Body[] bodies, Diagnostics diagnostics);
}
