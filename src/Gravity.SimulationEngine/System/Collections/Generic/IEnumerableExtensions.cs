// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Linq;

namespace System.Collections.Generic;

public static class IEnumerableExtensions
{
	#region Interface

	public static IEnumerable<T[]> Chunked<T>(this IEnumerable<T> enumerable, int chunkSize)
	{
		if(enumerable == null)
			throw new ArgumentNullException(nameof(enumerable));

		var ret = new List<T>(chunkSize);

		foreach(var item in enumerable)
		{
			ret.Add(item);

			if(ret.Count == chunkSize)
			{
				yield return ret.ToArray();

				ret.Clear();
			}
		}

		if(ret.Count != 0)
			yield return ret.ToArray();
	}

	public static IEnumerable<T> Except<T>(this IEnumerable<T> enumerable, T itemToExclude)
		=> enumerable.Where(i => !ReferenceEquals(i, itemToExclude));

	#endregion
}