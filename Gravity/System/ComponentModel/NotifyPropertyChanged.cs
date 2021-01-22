// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace System.ComponentModel
{
	public class NotifyPropertyChanged : INotifyPropertyChanged
	{
		#region Implementation of INotifyPropertyChanged

		public event PropertyChangedEventHandler PropertyChanged;

		#endregion

		#region Implementation

		protected bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, ref bool aChangedFlag, [CallerMemberName] string aPropertyName = null)
		{
			// ReSharper disable once ExplicitCallerInfoArgument
			if (!SetProperty(ref aStorage, aValue, aPropertyName))
				return false;

			aChangedFlag = true;
			return true;
		}

		protected bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, [CallerMemberName] string aPropertyName = null)
		{
			if (Equals(aStorage, aValue))
				return false;

			aStorage = aValue;

			// ReSharper disable ExplicitCallerInfoArgument
			RaisePropertyChanged(aPropertyName);
			// ReSharper restore ExplicitCallerInfoArgument

			return true;
		}

		protected void RaiseOtherPropertyChanged(string aPropertyName)
		{
			// ReSharper disable once ExplicitCallerInfoArgument
			RaisePropertyChanged(aPropertyName);
		}

		protected void RaisePropertyChanged([CallerMemberName] string aPropertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(aPropertyName));
		}

		#endregion
	}
}