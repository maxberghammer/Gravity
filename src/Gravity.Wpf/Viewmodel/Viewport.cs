// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Windows;
using Gravity.SimulationEngine;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class Viewport : NotifyPropertyChanged,
						IViewport
{
	#region Interface

	public DragIndicator? DragIndicator { get; set => SetProperty(ref field, value); }

	public Vector2D Center
		=> Size / 2 + TopLeft;

	public Vector2D Size
		=> BottomRight - TopLeft;

	public double Scale
	{
		get;
		set
		{
			value = Math.Min(7, value);

			if(!SetProperty(ref field, value))
				return;

			RaiseOtherPropertyChanged(nameof(ScaleFactor));
		}
	}

	public double ScaleFactor
		=> 1 / Math.Pow(10, Scale);

	// ReSharper disable once UnusedMember.Global
	public void CenterTo(Entity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var size = BottomRight - TopLeft;

		TopLeft = entity.Position - size / 2;
		BottomRight = entity.Position + size / 2;
	}

	public void Zoom(Vector2D zoomCenter, double zoomFactor)
	{
		var previousScaleFactor = ScaleFactor;
		var previousSize = Size;

		Scale = Math.Round(Scale + zoomFactor, 1);

		var newSize = previousSize / ScaleFactor * previousScaleFactor;
		var sizeDiff = newSize - previousSize;
		var zoomOffset = zoomCenter - Center;
		var newCenter = Center - new Vector2D(zoomOffset.X / previousSize.X * sizeDiff.X,
											  zoomOffset.Y / previousSize.Y * sizeDiff.Y);

		TopLeft = newCenter - newSize / 2;
		BottomRight = newCenter + newSize / 2;
	}

	public Vector2D ToWorld(Point viewportPoint)
		=> new Vector2D(viewportPoint.X, viewportPoint.Y) / ScaleFactor + TopLeft;

	public Point ToViewport(Vector2D worldVector)
	{
		var viewportVector = (worldVector - TopLeft) * ScaleFactor;

		return new(viewportVector.X, viewportVector.Y);
	}

	#endregion

	#region Implementation of IViewport

	public Vector2D TopLeft { get; set; }

	public Vector2D BottomRight { get; set; }

	#endregion
}