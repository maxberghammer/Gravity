using System.Numerics;
using System.Runtime.InteropServices;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	[StructLayout(LayoutKind.Sequential)]
	private struct BodyGpu
	{
		public Vector2 Position;
		public float Radius;
		public float StrokeWidth;

		public Vector3 FillColor;
		public uint Flags; // bit 0 = selected

		public Vector3 StrokeColor;
		private float _pad;
	}

	// Ortho-ähnliche Kamera: Welt -> Screen (TopLeft, Scale) -> NDC (ScreenSize)
	[StructLayout(LayoutKind.Sequential)]
	private struct CameraGpu
	{
		public Vector2 TopLeft; // 8
		public Vector2 ScreenSize; // 8 -> 16
		public float Scale; // 4 -> 20
		private float _pad0; // 4 -> 24
		private Vector2 _pad1; // 8 -> 32 (gesamt)
	}

	#endregion
}