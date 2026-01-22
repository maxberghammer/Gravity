using System.Collections.Generic;

namespace Gravity.Wpf.Viewmodel;

internal static class IEnumerableExtensions
{
	#region Internal types

	extension<T>(IReadOnlyCollection<T> source)
	{
		#region Interface

		public T[] ToArrayLocked(ref T[]? cache)
		{
			if(cache != null)
				return cache;

			lock(source)
			{
				cache = [.. source];

				return cache;
			}
		}

		#endregion
	}

	extension<T>(ICollection<T> source)
	{
		#region Interface

		public void AddLocked(T item)
		{
			lock(source)
				source.Add(item);
		}

		public void ClearLocked()
		{
			lock(source)
				source.Clear();
		}

		public void AddRangeLocked(IEnumerable<T> range)
		{
			lock(source)
				source.AddRange(range);
		}

		public void RemoveRangeLocked(IEnumerable<T> items)
		{
			lock(source)
				source.RemoveRange(items);
		}

		#endregion
	}

	#endregion
}