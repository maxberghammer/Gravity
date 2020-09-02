using System;
using System.ComponentModel;
using System.Windows;

namespace Gravity.Viewmodel
{
	internal class Viewport : NotifyPropertyChanged
	{
		#region Fields

		private double mScale;

		private Vector mTopLeft = VectorExtensions.Zero;

		private Vector mBottomRight = VectorExtensions.Zero;

		private DragIndicator mDragIndicator;

		#endregion

		#region Interface

		public DragIndicator DragIndicator { get => mDragIndicator; set => SetProperty(ref mDragIndicator, value); }

		public Vector TopLeft
		{
			get => mTopLeft;
			set
			{
				if (SetProperty(ref mTopLeft, value))
				{
					RaiseOtherPropertyChanged(nameof(Center));
					RaiseOtherPropertyChanged(nameof(Size));
				}
			}
		}

		public Vector BottomRight
		{
			get => mBottomRight;
			set
			{
				if (SetProperty(ref mBottomRight, value))
				{
					RaiseOtherPropertyChanged(nameof(Center));
					RaiseOtherPropertyChanged(nameof(Size));
				}
			}
		}

		public Vector Center
			=> Size / 2 + TopLeft;

		public Vector Size
			=> BottomRight - TopLeft;

		public double Scale
		{
			get => mScale;
			set
			{
				value = Math.Min(7, value);

				if (!SetProperty(ref mScale, value))
					return;

				RaiseOtherPropertyChanged(nameof(ScaleFactor));
			}
		}

		public double ScaleFactor
			=> 1 / Math.Pow(10, Scale);

		public void CenterTo(Entity aEntity)
		{
			var size = BottomRight - TopLeft;

			TopLeft = aEntity.Position - size / 2;
			BottomRight = aEntity.Position + size / 2;
		}

		public void Zoom(Vector aZoomCenter, double aZoomFactor)
		{
			var previousScaleFactor = ScaleFactor;
			var previousSize = Size;

			Scale = Math.Round(Scale + aZoomFactor, 1);

			var newSize = previousSize / ScaleFactor * previousScaleFactor;
			var sizeDiff = newSize - previousSize;
			var zoomOffset = aZoomCenter - Center;
			var newCenter = Center - new Vector(zoomOffset.X / previousSize.X * sizeDiff.X,
												zoomOffset.Y / previousSize.Y * sizeDiff.Y);

			TopLeft = newCenter - newSize / 2;
			BottomRight = newCenter + newSize / 2;
		}

		public Vector ToWorld(Point aViewportPoint)
			=> new Vector(aViewportPoint.X, aViewportPoint.Y) / ScaleFactor + TopLeft;

		public Point ToViewport(Vector aWorldVector)
			=> (Point)((aWorldVector - TopLeft) * ScaleFactor);

		#endregion
	}
}