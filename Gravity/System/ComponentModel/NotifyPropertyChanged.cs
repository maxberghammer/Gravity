using System.Runtime.CompilerServices;

namespace System.ComponentModel
{
	public class NotifyPropertyChanged : INotifyPropertyChanged
	{
		#region INotifyPropertyChanged overrides

		public event PropertyChangedEventHandler PropertyChanged;

		#endregion

		#region Implementation

		protected bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, [CallerMemberName] String aPropertyName = null)
		{
			if (Equals(aStorage, aValue))
				return false;

			aStorage = aValue;

			// ReSharper disable ExplicitCallerInfoArgument
			RaisePropertyChanged(aPropertyName);
			// ReSharper restore ExplicitCallerInfoArgument

			return true;
		}

        public bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, ref bool aChangedFlag, [CallerMemberName] String aPropertyName = null)
        {
            // ReSharper disable once ExplicitCallerInfoArgument
            if (!SetProperty(ref aStorage, aValue, aPropertyName))
                return false;

            aChangedFlag = true;
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