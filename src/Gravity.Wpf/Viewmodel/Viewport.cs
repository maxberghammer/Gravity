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

	public Vector3D Center
		=> Size / 2 + TopLeft;

	public Vector3D Size
		=> BottomRight - TopLeft;

	/// <summary>
	/// Depth of the 3D viewport. Change this property to modify the depth calculation.
	/// Currently equals height, creating a width×height×height cuboid.
	/// </summary>
	public double Depth
		=> Size.Y; // <- Hier ändern, um andere Tiefenberechnung zu verwenden

	/// <summary>
	/// Calculates the depth for a given height. Override this method to change the depth calculation.
	/// </summary>
	private static double CalculateDepthFromHeight(double height)
		=> height; // <- Hier ändern, um andere Tiefenberechnung zu verwenden

	/// <summary>
	/// Returns the 3D size with proper depth (width, height, depth).
	/// Use this instead of Size when you need the actual 3D bounds.
	/// </summary>
	public Vector3D Size3D
		=> new(Size.X, Size.Y, Depth);

	/// <summary>
	/// Returns half of the 3D size. Use this for centering operations.
	/// </summary>
	public Vector3D HalfSize3D
		=> Size3D / 2;

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

	/// <summary>
	/// Camera yaw angle in radians (rotation around Y axis)
	/// </summary>
	public double CameraYaw { get; set => SetProperty(ref field, value); }

	/// <summary>
	/// Camera pitch angle in radians (rotation around X axis, clamped to avoid gimbal lock)
	/// </summary>
	public double CameraPitch
	{
		get;
		set => SetProperty(ref field, Math.Clamp(value, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01));
	}

	/// <summary>
	/// Camera distance from center (for zoom in 3D view)
	/// </summary>
	public double CameraDistance
	{
		get;
		set => SetProperty(ref field, Math.Max(1, value));
	} = 1000;

	// ReSharper disable once UnusedMember.Global
	public void CenterTo(Body entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		SetBoundsAroundCenter(entity.Position, Size3D);
	}

	/// <summary>
	/// Sets the viewport bounds centered around the given point with the specified 3D size.
	/// The depth (Z) is automatically calculated from the height (Y) using CalculateDepthFromHeight.
	/// </summary>
	public void SetBoundsAroundCenter(Vector3D center, Vector3D size3D)
	{
		// Calculate depth from the NEW height, not the current Depth property
		var depth = CalculateDepthFromHeight(size3D.Y);
		var halfSize = new Vector3D(size3D.X / 2, size3D.Y / 2, depth / 2);
		TopLeft = center - halfSize;
		BottomRight = center + halfSize;
	}

	/// <summary>
	/// Sets the viewport bounds centered around the given point, keeping the current size.
	/// </summary>
	public void SetCenter(Vector3D center)
		=> SetBoundsAroundCenter(center, Size3D);

	public void Zoom(Vector3D zoomCenter, double zoomFactor)
	{
		var previousScaleFactor = ScaleFactor;
		var previousSize = Size3D;

		Scale = Math.Round(Scale + zoomFactor, 1);

		var scaleFactor = previousScaleFactor / ScaleFactor;
		var newWidth = previousSize.X * scaleFactor;
		var newHeight = previousSize.Y * scaleFactor;
		var newDepth = CalculateDepthFromHeight(newHeight);
		var newSize = new Vector3D(newWidth, newHeight, newDepth);
		
		var sizeDiff = newSize - previousSize;
		var zoomOffset = zoomCenter - Center;
		var newCenter = Center - new Vector3D(
			previousSize.X > 0 ? zoomOffset.X / previousSize.X * sizeDiff.X : 0,
			previousSize.Y > 0 ? zoomOffset.Y / previousSize.Y * sizeDiff.Y : 0,
			previousSize.Z > 0 ? zoomOffset.Z / previousSize.Z * sizeDiff.Z : 0);

		SetBoundsAroundCenter(newCenter, newSize);
	}

	/// <summary>
	/// Rotates the camera by the given delta angles
	/// </summary>
	public void RotateCamera(double deltaYaw, double deltaPitch)
	{
		CameraYaw += deltaYaw;
		CameraPitch += deltaPitch;
	}

	public Vector3D ToWorld(Point viewportPoint)
		=> new Vector3D(viewportPoint.X, viewportPoint.Y, 0) / ScaleFactor + TopLeft;

	public Point ToViewport(Vector3D worldVector)
	{
		var viewportVector = (worldVector - TopLeft) * ScaleFactor;

		return new(viewportVector.X, viewportVector.Y);
	}

	#endregion

	#region Implementation of IViewport

	public Vector3D TopLeft { get; set; }

	public Vector3D BottomRight { get; set; }

	#endregion
}