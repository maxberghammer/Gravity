using System.Numerics;
using System.Runtime.InteropServices;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct Vertex
	{
		#region Fields

		public readonly Vector3 Position;
		public readonly Vector3 Normal;
		
		#endregion

		#region Construction

		public Vertex(Vector3 pos, Vector3 nrm)
		{
			Position = pos;
			Normal = nrm;
		}

		#endregion
	}

	[StructLayout(LayoutKind.Sequential)]
	private readonly struct InstanceData
	{
		#region Fields

		public readonly Vector3 Position;
		public readonly float Radius;

		public readonly Vector4 FillColor;

		public readonly float StrokeWidth;
		public readonly Vector4 StrokeColor;

        #endregion

        #region Construction

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "<Pending>")]
        public InstanceData(Vector3 position,
							float radius,
							Vector4 fillColor,
							float strokeWidth,
							Vector4 strokeColor)
		{
			Position = position;
			Radius = radius;
			FillColor = fillColor;
			StrokeWidth = strokeWidth;
			StrokeColor = strokeColor;
		}

		#endregion
	}

	#endregion
}