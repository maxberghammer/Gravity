// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Windows;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

internal class DragIndicator : NotifyPropertyChanged
{
	#region Fields

	private double mDiameter;
	private Vector mEnd;
	private string? mLabel;
	private Vector mStart;

	#endregion

	#region Interface

	public Vector Start { get => mStart; set => SetProperty(ref mStart, value); }

	public Vector End { get => mEnd; set => SetProperty(ref mEnd, value); }

	public string? Label { get => mLabel; set => SetProperty(ref mLabel, value); }

	public double Diameter
	{
		get => mDiameter;
		set
		{
			if(SetProperty(ref mDiameter, value))
				RaiseOtherPropertyChanged(nameof(EntityTranslate));
		}
	}

	public double EntityTranslate
		=> -Diameter / 2;

	#endregion
}