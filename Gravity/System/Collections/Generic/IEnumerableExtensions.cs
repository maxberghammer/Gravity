// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Linq;

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic
{
	// ReSharper disable once InconsistentNaming
	internal static class IEnumerableExtensions
	{
		#region Interface

		public static IEnumerable<T[]> Chunked<T>(this IEnumerable<T> aEnumerable, int aChunkSize)
		{
			var ret = new List<T>(aChunkSize);

			foreach (var item in aEnumerable)
			{
				ret.Add(item);

				if (ret.Count == aChunkSize)
				{
					yield return ret.ToArray();
					ret.Clear();
				}
			}

			if (ret.Any())
				yield return ret.ToArray();
		}

		public static IEnumerable<T> Except<T>(this IEnumerable<T> aEnumerable, T aItemToExclude)
			=> aEnumerable.Where(i => !ReferenceEquals(i, aItemToExclude));

		#endregion
	}
}