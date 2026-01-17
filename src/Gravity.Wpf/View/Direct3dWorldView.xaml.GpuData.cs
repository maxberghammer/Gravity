using System.Numerics;
using System.Runtime.InteropServices;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	[StructLayout(LayoutKind.Sequential)]
	private struct BodyGpu
	{
		public Vector3 Position;  // 3D position
		public float Radius;
		
		public float StrokeWidth;
		public Vector3 FillColor;
		
		public uint Flags; // bit 0 = selected
		public Vector3 StrokeColor;
	}

	// 3D Orthogonal camera with View/Projection matrices
	[StructLayout(LayoutKind.Sequential)]
	private struct CameraGpu
	{
		public Matrix4x4 ViewProj;     // Combined View * Projection matrix (64 bytes)
		public Vector3 CameraRight;    // For billboard orientation
		public float _pad0;
		public Vector3 CameraUp;       // For billboard orientation
		public float _pad1;
		public Vector2 ScreenSize;     // Screen dimensions
		public float Scale;            // Zoom factor (world units per screen unit)
		public float _pad2;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PathParams
	{
		public uint PathVertexCount;
		private Vector3 _pad;
	}

	#endregion
}