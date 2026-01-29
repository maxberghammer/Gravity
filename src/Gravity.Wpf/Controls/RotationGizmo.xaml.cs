using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Gravity.Wpf.Controls;

/// <summary>
/// UserControl that displays a 3D rotation gizmo showing the current camera orientation.
/// Displays X (red), Y (green), and Z (blue) axes projected to 2D based on camera rotation.
/// </summary>
internal sealed partial class RotationGizmo : UserControl
{
	#region Fields

	public static readonly DependencyProperty CameraYawProperty =
		DependencyProperty.Register(nameof(CameraYaw), typeof(double), typeof(RotationGizmo),
			new PropertyMetadata(0.0, OnCameraRotationChanged));

	public static readonly DependencyProperty CameraPitchProperty =
		DependencyProperty.Register(nameof(CameraPitch), typeof(double), typeof(RotationGizmo),
			new PropertyMetadata(0.0, OnCameraRotationChanged));

	private const double AxisLength = 50;
	private const double CenterX = 60;
	private const double CenterY = 60;

	#endregion

	#region Construction

	public RotationGizmo()
	{
		InitializeComponent();
		UpdateGizmo();
	}

	#endregion

	#region Properties

	/// <summary>
	/// Camera yaw angle in radians (rotation around Y axis).
	/// </summary>
	public double CameraYaw
	{
		get => (double)GetValue(CameraYawProperty);
		set => SetValue(CameraYawProperty, value);
	}

	/// <summary>
	/// Camera pitch angle in radians (rotation around X axis).
	/// </summary>
	public double CameraPitch
	{
		get => (double)GetValue(CameraPitchProperty);
		set => SetValue(CameraPitchProperty, value);
	}

	#endregion

	#region Implementation

	private static void OnCameraRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if(d is RotationGizmo gizmo)
			gizmo.UpdateGizmo();
	}

	private void UpdateGizmo()
	{
		var yaw = CameraYaw;
		var pitch = CameraPitch;

		// Project 3D axes to 2D using the camera rotation
		var cosYaw = Math.Cos(yaw);
		var sinYaw = Math.Sin(yaw);
		var cosPitch = Math.Cos(pitch);
		var sinPitch = Math.Sin(pitch);

		// X axis (red) - points right in world space
		var xEndX = CenterX + AxisLength * cosYaw;
		var xEndY = CenterY + AxisLength * sinYaw * sinPitch;
		_gizmoAxisX.SetCurrentValue(Line.X2Property, xEndX);
		_gizmoAxisX.SetCurrentValue(Line.Y2Property, xEndY);

		// Y axis (green) - points up in world space
		var yEndX = CenterX;
		var yEndY = CenterY - AxisLength * cosPitch;
		_gizmoAxisY.SetCurrentValue(Line.X2Property, yEndX);
		_gizmoAxisY.SetCurrentValue(Line.Y2Property, yEndY);

		// Z axis (blue) - points towards viewer in world space
		var zEndX = CenterX + AxisLength * sinYaw;
		var zEndY = CenterY - AxisLength * cosYaw * sinPitch;
		_gizmoAxisZ.SetCurrentValue(Line.X2Property, zEndX);
		_gizmoAxisZ.SetCurrentValue(Line.Y2Property, zEndY);

		// Update angle text
		var yawDeg = yaw * 180 / Math.PI;
		var pitchDeg = pitch * 180 / Math.PI;
		_gizmoAngleText.SetCurrentValue(TextBlock.TextProperty, $"Yaw: {yawDeg:F1}°  Pitch: {pitchDeg:F1}°");
	}

	#endregion
}
