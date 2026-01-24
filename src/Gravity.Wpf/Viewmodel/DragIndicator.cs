// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Windows;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class DragIndicator : NotifyPropertyChanged
{
	#region Interface

	public Point Start { get; set => SetProperty(ref field, value); }

	public Point End { get; set => SetProperty(ref field, value); }

	public string? Label { get; set => SetProperty(ref field, value); }

	public double Diameter
	{
		get;
		set
		{
			if(SetProperty(ref field, value))
				RaiseOtherPropertyChanged(nameof(BodyTranslate));
		}
	}

	public double BodyTranslate
		=> -Diameter / 2;

	#endregion
}