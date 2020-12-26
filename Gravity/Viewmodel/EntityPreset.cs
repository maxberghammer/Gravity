using System;
using System.Windows.Media;

namespace Gravity.Viewmodel
{
	internal class EntityPreset
	{
		#region Construction

		// ReSharper disable InconsistentNaming
		public EntityPreset(string aName, double am, double ar, SolidColorBrush aFill, Guid aId)
			// ReSharper restore InconsistentNaming
			: this(aName, am, ar, aFill, null, 0, aId)
		{
		}

		// ReSharper disable InconsistentNaming
		public EntityPreset(string aName, double am, double ar, SolidColorBrush aFill, SolidColorBrush aStroke, double aStrokeWidth, Guid aId)
			// ReSharper restore InconsistentNaming
		{
			Name = aName;
			m = am;
			r = ar;
			Fill = aFill;
			Stroke = aStroke;
			StrokeWidth = aStrokeWidth;
			Id = aId;
		}

		public static EntityPreset FromDensity(string aName, double aDensity, double ar, SolidColorBrush aFill, SolidColorBrush aStroke, double aStrokeWidth, Guid aId)
			=> new EntityPreset(aName, 4.0d / 3.0d * Math.Pow(ar, 3) * Math.PI * aDensity, ar, aFill, aStroke, aStrokeWidth, aId);

		#endregion

		#region Interface

		public string Name { get; }

		// ReSharper disable once InconsistentNaming
		public double m { get; }

		// ReSharper disable once InconsistentNaming
		public double r { get; }

		public SolidColorBrush Fill { get; }

		public SolidColorBrush Stroke { get; }

		public double StrokeWidth { get; }

		public Guid Id { get; }

		#endregion
	}
}