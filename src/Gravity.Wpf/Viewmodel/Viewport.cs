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
	#region Fields

	private const double _snapAngle = Math.PI / 4; // 45 degrees
	private double _rawPitch;

	// Raw (unsnapped) rotation values for smooth snap behavior
	private double _rawYaw;

	#endregion

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
	/// Returns the 3D size with proper depth (width, height, depth).
	/// Use this instead of Size when you need the actual 3D bounds.
	/// </summary>
	public Vector3D Size3D
		=> new(Size.X, Size.Y, Depth);

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
	public double CameraPitch { get; set => SetProperty(ref field, Math.Clamp(value, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01)); }

	/// <summary>
	/// Camera distance from center (for zoom in 3D view)
	/// </summary>
	public double CameraDistance { get; set => SetProperty(ref field, Math.Max(1, value)); } = 1000;

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
		var previousCenter = Center;

		Scale = Math.Round(Scale + zoomFactor, 1);

		var sizeRatio = previousScaleFactor / ScaleFactor;
		var newWidth = previousSize.X * sizeRatio;
		var newHeight = previousSize.Y * sizeRatio;
		var newDepth = CalculateDepthFromHeight(newHeight);
		var newSize = new Vector3D(newWidth, newHeight, newDepth);

		// Zoom to cursor: keep the zoom center point at the same screen position
		// The center moves towards/away from the zoom point proportionally to the size change
		var newCenter = zoomCenter + (previousCenter - zoomCenter) * sizeRatio;

		SetBoundsAroundCenter(newCenter, newSize);
	}

	/// <summary>
	/// Rotates the camera by the given delta angles. When snap is true, the rotation snaps to 45° increments.
	/// </summary>
	public void RotateCamera(double deltaYaw, double deltaPitch, bool snap = false)
	{
		if(snap)
		{
			// Accumulate raw rotation
			_rawYaw += deltaYaw;
			_rawPitch += deltaPitch;

			// Snap to 45° increments
			CameraYaw = SnapToAngle(_rawYaw, _snapAngle);
			CameraPitch = SnapToAngle(_rawPitch, _snapAngle);
		}
		else
		{
			// Normal rotation
			CameraYaw += deltaYaw;
			CameraPitch += deltaPitch;

			// Keep raw values in sync
			_rawYaw = CameraYaw;
			_rawPitch = CameraPitch;
		}
	}

	/// <summary>
	/// Converts a viewport point to world coordinates, taking camera rotation into account.
	/// The resulting point lies on a plane through the viewport center, perpendicular to the camera view direction.
	/// </summary>
	public Vector3D ToWorld(Point viewportPoint)
	{
		// Calculate offset from viewport center in screen space
		var screenCenterX = Size.X * ScaleFactor / 2;
		var screenCenterY = Size.Y * ScaleFactor / 2;
		var offsetX = (viewportPoint.X - screenCenterX) / ScaleFactor;
		var offsetY = (viewportPoint.Y - screenCenterY) / ScaleFactor;

		// Calculate camera basis vectors using standard rotation convention:
		// Yaw rotates around global Y-axis, Pitch rotates around local X-axis (Right)
		var cosYaw = Math.Cos(CameraYaw);
		var sinYaw = Math.Sin(CameraYaw);
		var cosPitch = Math.Cos(CameraPitch);
		var sinPitch = Math.Sin(CameraPitch);

		// Right vector (camera X axis) - only affected by yaw
		var rightX = cosYaw;
		var rightY = 0.0;
		var rightZ = sinYaw;

		// Up vector (camera Y axis) - affected by both yaw and pitch
		// Derived from rotating (0,1,0) around the Right axis by -pitch
		var upX = sinYaw * sinPitch;
		var upY = cosPitch;
		var upZ = -cosYaw * sinPitch;

		// Calculate world position: center + offset in camera space
		// offsetX moves along Right, offsetY moves opposite to Up (screen Y is inverted)
		var worldX = Center.X + offsetX * rightX - offsetY * upX;
		var worldY = Center.Y + offsetX * rightY - offsetY * upY;
		var worldZ = Center.Z + offsetX * rightZ - offsetY * upZ;

		return new(worldX, worldY, worldZ);
	}

	/// <summary>
	/// Converts world coordinates to viewport point, taking camera rotation into account.
	/// </summary>
	public Point ToViewport(Vector3D worldVector)
	{
		// Calculate offset from center in world space
		var offsetWorld = worldVector - Center;

		// Calculate camera basis vectors (same as in ToWorld)
		var cosYaw = Math.Cos(CameraYaw);
		var sinYaw = Math.Sin(CameraYaw);
		var cosPitch = Math.Cos(CameraPitch);
		var sinPitch = Math.Sin(CameraPitch);

		// Right vector
		var rightX = cosYaw;
		var rightZ = sinYaw;

		// Up vector
		var upX = sinYaw * sinPitch;
		var upY = cosPitch;
		var upZ = -cosYaw * sinPitch;

		// Project onto camera right vector (dot product) -> screen X
		var screenX = offsetWorld.X * rightX + offsetWorld.Z * rightZ;

		// Project onto camera up vector (dot product), negated for screen Y
		var screenY = -(offsetWorld.X * upX + offsetWorld.Y * upY + offsetWorld.Z * upZ);

		// Convert to viewport coordinates
		var screenCenterX = Size.X * ScaleFactor / 2;
		var screenCenterY = Size.Y * ScaleFactor / 2;

		return new(screenCenterX + screenX * ScaleFactor, screenCenterY + screenY * ScaleFactor);
	}

	#endregion

	#region Implementation of IViewport

	public Vector3D TopLeft { get; set; }

	public Vector3D BottomRight { get; set; }

	#endregion

	#region Implementation

	/// <summary>
	/// Calculates the depth for a given height. Override this method to change the depth calculation.
	/// </summary>
	private static double CalculateDepthFromHeight(double height)
		=> height; // <- Hier ändern, um andere Tiefenberechnung zu verwenden

	/// <summary>
	/// Snaps an angle to the nearest multiple of snapAngle
	/// </summary>
	private static double SnapToAngle(double angle, double snapAngle)
		=> Math.Round(angle / snapAngle) * snapAngle;

	#endregion
}