using System.Numerics;
using System.Runtime.InteropServices;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	// GPU struct - field order doesn't matter thanks to explicit offsets
	[StructLayout(LayoutKind.Explicit, Size = 48)]
	private struct BodyGpu
	{
		#region Fields

		[FieldOffset(20)] public Vector3 FillColor; // 12 bytes
		[FieldOffset(32)] public uint Flags; // 4 bytes (bit 0 = selected)
		[FieldOffset(0)] public Vector3 Position; // 12 bytes
		[FieldOffset(12)] public float Radius; // 4 bytes
		[FieldOffset(36)] public Vector3 StrokeColor; // 12 bytes
		[FieldOffset(16)] public float StrokeWidth; // 4 bytes

		#endregion
	}

	// GPU cbuffer - field order doesn't matter thanks to explicit offsets
	[StructLayout(LayoutKind.Explicit, Size = 112)]
	private struct CameraGpu
	{
		#region Fields

		[FieldOffset(76)] public float _pad0; // 4 bytes
		[FieldOffset(92)] public float _pad1; // 4 bytes
		[FieldOffset(108)] public float _pad2; // 4 bytes
		[FieldOffset(64)] public Vector3 CameraRight; // 12 bytes - For billboard orientation
		[FieldOffset(80)] public Vector3 CameraUp; // 12 bytes - For billboard orientation
		[FieldOffset(104)] public float Scale; // 4 bytes - Zoom factor
		[FieldOffset(96)] public Vector2 ScreenSize; // 8 bytes - Screen dimensions
		[FieldOffset(0)] public Matrix4x4 ViewProj; // 64 bytes - Combined View * Projection matrix

		#endregion
	}

	// GPU cbuffer - field order doesn't matter thanks to explicit offsets
	[StructLayout(LayoutKind.Explicit, Size = 16)]
	private struct PathParams
	{
		#region Fields

		[FieldOffset(0)] public uint PathVertexCount; // 4 bytes
		[FieldOffset(4)] private Vector3 _pad; // 12 bytes

		#endregion
	}

	#endregion
}