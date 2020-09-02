using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.ComponentModel
{
	public static class INotifyPropertyChangedExtensions
	{
		public static bool SetProperty<T>(this INotifyPropertyChanged aTarget, ref T aStorage, T aValue, [CallerMemberName] string aPropertyName = null)
		{
			if (Equals(aStorage, aValue))
				return false;

			aStorage = aValue;

			// ReSharper disable once ExplicitCallerInfoArgument
			aTarget.RaisePropertyChanged(aPropertyName);

			return true;
		}

		public static void RaisePropertyChanged(this INotifyPropertyChanged aTarget, [CallerMemberName] string aPropertyName = null)
		{
			var eventFieldInfo = aTarget.GetType().GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);

			((Delegate)eventFieldInfo?.GetValue(aTarget))?.DynamicInvoke(aTarget, new PropertyChangedEventArgs(aPropertyName));
		}
	}
}
