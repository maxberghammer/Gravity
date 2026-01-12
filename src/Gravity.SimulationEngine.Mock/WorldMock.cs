namespace Gravity.SimulationEngine.Mock;

public sealed record WorldMock(IViewport Viewport, bool ClosedBoundaries, bool ElasticCollisions) : IWorld;