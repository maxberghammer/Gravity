namespace Gravity.SimulationEngine.Mock;

public sealed record ViewportMock(Vector2D TopLeft, Vector2D BottomRight) : IViewport;