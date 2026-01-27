namespace Gravity.SimulationEngine.Mock;

public sealed record ViewportMock(Vector3D TopLeft, Vector3D BottomRight) : IViewport;