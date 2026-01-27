using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine.Mock;

[SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "Mock")]
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Mock")]
internal sealed record WorldMock(bool ClosedBoundaries, bool ElasticCollisions, Body[] Bodies, double Timescale = 1.0) : IWorld
{
	#region Implementation of IWorld

	/// <inheritdoc/>
	public IReadOnlyList<Body> GetBodies()
		=> Bodies;

	/// <inheritdoc/>
	public void RemoveBodies(IReadOnlyCollection<Body> bodies)
	{
		// Mock implementation - does nothing
	}

	#endregion
}