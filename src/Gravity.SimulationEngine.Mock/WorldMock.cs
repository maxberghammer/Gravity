using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine.Mock;

[SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "Mock")]
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Mock")]
internal sealed record WorldMock(IViewport Viewport, bool ClosedBoundaries, bool ElasticCollisions, Body[] Entities) : IWorld
{
	/// <inheritdoc />
	public Body[] GetEntities()
		=> Entities;
}