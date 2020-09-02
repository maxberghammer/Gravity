using System.ComponentModel;
using System.Windows;

namespace Gravity.Viewmodel
{
	internal class DragIndicator : NotifyPropertyChanged
	{
		#region Fields

		private Vector mStart;

		private Vector mEnd;

		private string mLabel;

		#endregion

		#region Interface

		public Vector Start { get => mStart; set => SetProperty(ref mStart, value); }

		public Vector End { get => mEnd; set => SetProperty(ref mEnd, value); }

		public string Label { get => mLabel; set => SetProperty(ref mLabel, value); }

		#endregion
	}
}