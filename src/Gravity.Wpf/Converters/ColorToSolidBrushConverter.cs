using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = Gravity.SimulationEngine.Color;

namespace Gravity.Wpf.Converters;

internal sealed class ColorToSolidBrushConverter : IValueConverter
{
	#region Implementation of IValueConverter

	object IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value switch
		   {
			   Color c => new SolidColorBrush(System.Windows.Media.Color.FromArgb(Math.Clamp(c.A, (byte)0, (byte)255), Math.Clamp(c.R, (byte)0, (byte)255),
																				  Math.Clamp(c.G, (byte)0, (byte)255), Math.Clamp(c.B, (byte)0, (byte)255)))
						  {
							  Opacity = c.A / 255.0
						  },
			   System.Windows.Media.Color wc => new SolidColorBrush(wc),
			   var _                         => Binding.DoNothing
		   };

	object IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		switch(value)
		{
			case SolidColorBrush brush:
			{
				var wc = brush.Color;
				var a = (byte)Math.Round(brush.Opacity * 255.0);

				return new Color(a, wc.R, wc.G, wc.B);
			}
			default: return Binding.DoNothing;
		}
	}

	#endregion
}