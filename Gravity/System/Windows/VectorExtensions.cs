// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

// ReSharper disable once CheckNamespace
namespace System.Windows
{
	public static class VectorExtensions
	{
		#region Fields

		public static readonly Vector Zero = new(0, 0);

		#endregion

		#region Interface

		public static Vector Unit(this Vector aThis)
			=> aThis / aThis.Length;

		public static Vector Norm(this Vector aThis)
			=> new(aThis.Y, -aThis.X);

		// ReSharper disable once UnusedMember.Global
		public static Vector Round(this Vector aThis, int aDecimals)
			=> new(Math.Round(aThis.X, aDecimals), Math.Round(aThis.Y, aDecimals));

		#endregion
	}
}