using System;
using System.Windows.Media;

namespace Gravity.Viewmodel
{
	internal class EntityPreset
	{
		#region Construction

		// ReSharper disable InconsistentNaming
		public EntityPreset(string aName, double am, double ar, Brush aFill)
			// ReSharper restore InconsistentNaming
			: this(aName, am, ar, aFill, null, 0)
		{
		}

		// ReSharper disable InconsistentNaming
		public EntityPreset(string aName, double am, double ar, Brush aFill, Brush aStroke, double aStrokeWidth)
			// ReSharper restore InconsistentNaming
		{
			Name = aName;
			m = am;
			r = ar;
			Fill = aFill;
			Stroke = aStroke;
			StrokeWidth = aStrokeWidth;
		}

		public static EntityPreset FromDensity(string aName, double aDensity, double ar, Brush aFill, Brush aStroke, double aStrokeWidth)
			=> new EntityPreset(aName, 4.0d / 3.0d * Math.Pow(ar, 3) * Math.PI * aDensity, ar, aFill, aStroke, aStrokeWidth);

		#endregion

		#region Interface

		public string Name { get; }

		// ReSharper disable once InconsistentNaming
		public double m { get; }

		// ReSharper disable once InconsistentNaming
		public double r { get; }

		public Brush Fill { get; }

		public Brush Stroke { get; }

		public double StrokeWidth { get; }

		#endregion
	}
}