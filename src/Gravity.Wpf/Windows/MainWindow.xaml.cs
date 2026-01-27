using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;
using Microsoft.Win32;

namespace Gravity.Wpf.Windows;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed partial class MainWindow
{
	#region Fields

	private const double _cameraRotationSensitivity = 0.005;
	private const double _displayFrequencyInHz = 60;
	private const string _stateFileExtension = "grv";
	private const int _viewportSelectionSearchRadius = 30;
	private static Vector3D? _referencePosition;
	private Point? _lastMousePosition;
	private bool _wasSimulationRunning;

	#endregion

	#region Construction

	public MainWindow(IApplication application)
	{
		InitializeComponent();

		DataContext = new Main(application,
							   new(application.World)
							   {
								   ClosedBoundaries = true,
								   ElasticCollisions = true,
								   LogarithmicTimescale = 0
							   },
							   new(application.Viewport)
							   {
								   Autocenter = false,
								   Scale = 0
							   })
					  {
						  SelectedBodyPreset = application.BodyPresets[0],
						  SelectedEngineType = application.EngineTypes[0],
						  ShowPath = true,
						  SimulationFrequencyInHz = _displayFrequencyInHz,
						  DisplayFrequencyInHz = _displayFrequencyInHz
					  };
	}

	#endregion

	#region Implementation

	private Main Viewmodel
		=> (Main)DataContext;

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseDown(object sender, MouseButtonEventArgs args)
	{
		var viewportPoint = args.GetPosition((IInputElement)sender);

		_wasSimulationRunning = Viewmodel.IsSimulationRunning;
		Viewmodel.IsSimulationRunning = false;

		if(Keyboard.Modifiers == ModifierKeys.None &&
		   args.LeftButton == MouseButtonState.Pressed)
		{
			Viewmodel.SelectedBody = Viewmodel.Application.FindClosestBody(Vector2.Create(viewportPoint), _viewportSelectionSearchRadius);

			if(null != Viewmodel.SelectedBody)
			{
				var entityViewportPoint = Viewmodel.Viewport
												   .ToViewport(Viewmodel.SelectedBody.Position);

				Viewmodel.DragIndicator = new()
										  {
											  Start = entityViewportPoint,
											  End = entityViewportPoint,
											  Diameter = Viewmodel.Application
																  .Viewport
																  .ToViewport((Viewmodel.SelectedBody.r + Viewmodel.SelectedBody.AtmosphereThickness) * 2)
										  };

				return;
			}
		}

		_referencePosition = Viewmodel.Viewport.ToWorld(viewportPoint);

		Viewmodel.DragIndicator = new()
								  {
									  Start = viewportPoint,
									  End = viewportPoint,
									  Diameter = Viewmodel.Application
														  .Viewport
														  .ToViewport((Viewmodel.SelectedBodyPreset.r + Viewmodel.SelectedBodyPreset.AtmosphereThickness) * 2)
								  };
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseUp(object sender, MouseButtonEventArgs args)
		// Reset last mouse position when any button is released
		=> _lastMousePosition = null;

	private void OnWorldMouseMove(object sender, MouseEventArgs args)
	{
		var viewportPoint = args.GetPosition((IInputElement)sender);
		var worldPos = Viewmodel.Viewport.ToWorld(viewportPoint);

		// Update mouse coordinates display
		_lblMouseCoordinates.Content = $"X: {worldPos.X:F1}  Y: {worldPos.Y:F1}  Z: {worldPos.Z:F1}";

		// Camera rotation with R key + mouse movement - always rotate around viewport center
		if(Keyboard.IsKeyDown(Key.R))
		{
			UpdateRotationGizmo(true);

			if(_lastMousePosition.HasValue)
			{
				var deltaX = viewportPoint.X - _lastMousePosition.Value.X;
				var deltaY = viewportPoint.Y - _lastMousePosition.Value.Y;

				// Snap to 45° increments when Shift is held
				var snap = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

				Viewmodel.Application.Viewport.RotateCamera(deltaX * _cameraRotationSensitivity, deltaY * _cameraRotationSensitivity, snap);
			}

			_lastMousePosition = viewportPoint;

			return;
		}

		_lastMousePosition = null;
		UpdateRotationGizmo(false);

		if(null == Viewmodel.DragIndicator)
			return;

		Viewmodel.DragIndicator.End = viewportPoint;

		var position = Viewmodel.Viewport.ToWorld(viewportPoint);

		if(_referencePosition.HasValue)
		{
			var velocity = Viewmodel.CalculateVelocityPerSimulationStep(_referencePosition.Value, position);

			Viewmodel.DragIndicator.Label = args.RightButton == MouseButtonState.Pressed
												? $"Δv={velocity}m/s"
												: $"v={velocity}m/s";

			return;
		}

		if(null != Viewmodel.SelectedBody)
		{
			var velocity = Viewmodel.CalculateVelocityPerSimulationStep(Viewmodel.SelectedBody.Position, position);

			Viewmodel.DragIndicator.Label = Keyboard.IsKeyDown(Key.LeftAlt)
												? $"Δv={velocity}m/s"
												: $"v={velocity}m/s";
		}
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
	{
		Viewmodel.IsSimulationRunning = _wasSimulationRunning;

		var referencePosition = _referencePosition;
		var viewportPoint = args.GetPosition((IInputElement)sender);
		var position = Viewmodel.Viewport.ToWorld(viewportPoint);

		Viewmodel.DragIndicator = null;
		_referencePosition = null;

		if(null != referencePosition)
		{
			if(Keyboard.IsKeyDown(Key.LeftAlt))
			{
				Viewmodel.Application.AddRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), false);

				return;
			}

			Viewmodel.Application.AddBody(referencePosition.Value, Viewmodel.CalculateVelocityPerSimulationStep(referencePosition.Value, position));
			Viewmodel.Application.StopRespawn();

			return;
		}

		if(null == Viewmodel.SelectedBody)
			return;

		// Check if mouse moved significantly in viewport space (2D on screen)
		// Not in world space (3D), since the body could be at any Z depth
		var bodyViewportPoint = Viewmodel.Application
										 .Viewport
										 .ToViewport(Viewmodel.SelectedBody.Position);
		var viewportDistance = Math.Sqrt(Math.Pow(viewportPoint.X - bodyViewportPoint.X, 2) +
										 Math.Pow(viewportPoint.Y - bodyViewportPoint.Y, 2));

		if(viewportDistance <= _viewportSelectionSearchRadius)
			return; // Just a click, no drag - keep selection

		var velocity = Viewmodel.CalculateVelocityPerSimulationStep(Viewmodel.SelectedBody.Position, position);

		// Dragged: change velocity
		if(Keyboard.IsKeyDown(Key.LeftAlt))
			Viewmodel.SelectedBody.v += velocity;
		else
			Viewmodel.SelectedBody.v = velocity;
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldRightButtonUp(object sender, MouseButtonEventArgs args)
	{
		Viewmodel.IsSimulationRunning = _wasSimulationRunning;

		var referencePosition = _referencePosition;
		var position = Viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		Viewmodel.DragIndicator = null;
		_referencePosition = null;

		if(null == referencePosition)
			return;

		if(Keyboard.IsKeyDown(Key.LeftAlt))
		{
			Viewmodel.Application.AddRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), true);

			return;
		}

		Viewmodel.Application.AddOrbitBody(referencePosition.Value, Viewmodel.CalculateVelocityPerSimulationStep(referencePosition.Value, position));
		Viewmodel.Application.StopRespawn();
	}

	private void OnWorldSizeChanged(object sender, SizeChangedEventArgs args)
		=> Viewmodel.Application
					.Viewport
					.Resize(new(Viewmodel.Application.Viewport.ToWorld((float)args.NewSize.Width),
								Viewmodel.Application.Viewport.ToWorld((float)args.NewSize.Height),
								Viewmodel.Application.Viewport.ToWorld((float)args.NewSize.Height)));

	private void OnResetClicked(object sender, RoutedEventArgs args)
		=> Viewmodel.Application
					.Reset();

	private void OnWorldMouseWheel(object sender, MouseWheelEventArgs args)
		=> Viewmodel.Application
					.Viewport
					.Zoom(Viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender)),
						  -Math.Sign(args.Delta) * (Keyboard.IsKeyDown(Key.LeftAlt)
														? 0.1
														: 1));

	private void OnAutoScaleAndCenterViewportClicked(object sender, RoutedEventArgs args)
		=> Viewmodel.Application
					.Viewport
					.AutoScaleAndCenter();

	private void OnBodyPresetSelectionChanged(object sender, SelectionChangedEventArgs args)
		=> Viewmodel.IsBodyPresetSelectionVisible = false;

	[SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
	private async void OnSaveClicked(object sender, RoutedEventArgs args)
	{
		var dlg = new SaveFileDialog
				  {
					  DefaultExt = _stateFileExtension,
					  Filter = $"Gravity Files | *.{_stateFileExtension}"
				  };
		var dlgResult = dlg.ShowDialog(this);

		if(dlgResult is not true)
			return;

		await Viewmodel.Application
					   .SaveAsync(dlg.FileName);
	}

	[SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
	private async void OnOpenClicked(object sender, RoutedEventArgs args)
	{
		var dlg = new OpenFileDialog
				  {
					  DefaultExt = _stateFileExtension,
					  Filter = $"Gravity Files | *.{_stateFileExtension}"
				  };
		var dlgResult = dlg.ShowDialog(this);

		if(dlgResult is not true)
			return;

		await Viewmodel.Application
					   .OpenAsync(dlg.FileName);
	}

	private void OnEngineTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
		=> Viewmodel.IsEngineSelectionVisible = false;

	private void OnWorldKeyDown(object sender, KeyEventArgs e)
	{
		if(e.Key == Key.R)
			UpdateRotationGizmo(true);
	}

	private void OnWorldKeyUp(object sender, KeyEventArgs e)
	{
		if(e.Key == Key.R)
			UpdateRotationGizmo(false);
	}

	private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if(e.Key == Key.R)
			UpdateRotationGizmo(true);
	}

	private void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
	{
		if(e.Key == Key.R)
			UpdateRotationGizmo(false);
	}

	private void UpdateRotationGizmo(bool visible)
	{
		_rotationGizmo.Visibility = visible
										? Visibility.Visible
										: Visibility.Collapsed;

		if(!visible)
			return;

		var yaw = Viewmodel.Application.Viewport.CurrentCameraYaw;
		var pitch = Viewmodel.Application.Viewport.CurrentCameraPitch;
		const double axisLength = 50;
		const double centerX = 60;
		const double centerY = 60;

		// Project 3D axes to 2D using the camera rotation
		var cosYaw = Math.Cos(yaw);
		var sinYaw = Math.Sin(yaw);
		var cosPitch = Math.Cos(pitch);
		var sinPitch = Math.Sin(pitch);

		// X axis (red) - points right in world space
		var xEndX = centerX + axisLength * cosYaw;
		var xEndY = centerY + axisLength * sinYaw * sinPitch;
		_gizmoAxisX.X2 = xEndX;
		_gizmoAxisX.Y2 = xEndY;

		// Y axis (green) - points up in world space
		var yEndX = centerX;
		var yEndY = centerY - axisLength * cosPitch;
		_gizmoAxisY.X2 = yEndX;
		_gizmoAxisY.Y2 = yEndY;

		// Z axis (blue) - points towards viewer in world space
		var zEndX = centerX + axisLength * sinYaw;
		var zEndY = centerY - axisLength * cosYaw * sinPitch;
		_gizmoAxisZ.X2 = zEndX;
		_gizmoAxisZ.Y2 = zEndY;

		// Update angle text
		var yawDeg = yaw * 180 / Math.PI;
		var pitchDeg = pitch * 180 / Math.PI;
		_gizmoAngleText.Text = $"Yaw: {yawDeg:F1}°  Pitch: {pitchDeg:F1}°";
	}

	#endregion
}