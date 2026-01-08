using System.Numerics;
using System.Runtime.InteropServices;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	[StructLayout(LayoutKind.Sequential)]
	private struct EntityGpu
	{
		public Vector2 Position;
		public float Radius;
		public float StrokeWidth;

		public Vector3 FillColor;
		public uint Flags; // bit 0 = selected

		public Vector3 StrokeColor;
		private float _pad;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct CameraCB
	{
		public Vector2 ViewCenter;
		public float ViewScale;
		public float Aspect;
	}

	#endregion
}