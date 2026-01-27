namespace Gravity.SimulationEngine;

public interface IViewport
{
	Vector3D TopLeft { get; }

	Vector3D BottomRight { get; }
}