using System;
using System.Windows;

namespace Gravity.Viewmodel
{
	internal static class VectorExtensions
	{
		#region Fields

		public static readonly Vector Zero = new Vector(0, 0);

		#endregion

		#region Interface

		public static Vector Unit(this Vector aThis)
			=> aThis / aThis.Length;

		public static Vector Norm(this Vector aThis)
			=> new Vector(aThis.Y, -aThis.X);

		public static Vector Round(this Vector aThis, int aDecimals)
			=> new Vector(Math.Round(aThis.X, aDecimals), Math.Round(aThis.Y, aDecimals));

		#endregion
	}
}