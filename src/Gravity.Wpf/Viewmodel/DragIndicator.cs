// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Windows;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class DragIndicator : NotifyPropertyChanged
{
	#region Interface

	public Vector Start { get; set => SetProperty(ref field, value); }

	public Vector End { get; set => SetProperty(ref field, value); }

	public string? Label { get; set => SetProperty(ref field, value); }

	public double Diameter
	{
		get;
		set
		{
			if(SetProperty(ref field, value))
				RaiseOtherPropertyChanged(nameof(EntityTranslate));
		}
	}

	public double EntityTranslate
		=> -Diameter / 2;

	#endregion
}