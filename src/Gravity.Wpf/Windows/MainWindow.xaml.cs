using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;

namespace Gravity.Wpf.Windows;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed partial class MainWindow
{
	#region Fields

	private const double _cameraPanningSensitivity = 1.0;
	private const double _cameraRotationSensitivity = 0.005;
	private const double _displayFrequencyInHz = 60;
	private const int _viewportSelectionSearchRadius = 30;
	private static Vector3D? _referencePosition;
	private Point? _lastMousePosition;
	private bool _wasSimulationRunning;
	private FrameworkElement? _worldView;

	#endregion

	#region Construction

	public MainWindow(IApplication application)
	{
		InitializeComponent();

        DataContext = new Viewmodel.Application(application,
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

	private Viewmodel.Application Viewmodel
		=> (Viewmodel.Application)DataContext;

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseDown(object sender, MouseButtonEventArgs args)
	{
		var viewportPoint = args.GetPosition((IInputElement)sender);

		_wasSimulationRunning = Viewmodel.IsSimulationRunning;
		Viewmodel.IsSimulationRunning = false;

		if(Keyboard.Modifiers == ModifierKeys.None &&
		   args.LeftButton == MouseButtonState.Pressed)
		{
			Viewmodel.SelectedBody = Viewmodel.Domain.FindClosestBody(Vector2.Create(viewportPoint), _viewportSelectionSearchRadius);

			if(null != Viewmodel.SelectedBody)
			{
				var entityViewportPoint = Viewmodel.Viewport
												   .ToViewport(Viewmodel.SelectedBody.Position);

				Viewmodel.DragIndicator = new()
										  {
											  Start = entityViewportPoint,
											  End = entityViewportPoint,
											  Diameter = Viewmodel.Domain
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
									  Diameter = Viewmodel.Domain
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
		_worldView ??= (FrameworkElement)sender;

		var viewportPoint = args.GetPosition((IInputElement)sender);
		var worldPos = Viewmodel.Viewport.ToWorld(viewportPoint);

		// Update mouse coordinates display
		_lblMouseCoordinates.SetCurrentValue(ContentProperty, $"X: {worldPos.X:F1}  Y: {worldPos.Y:F1}  Z: {worldPos.Z:F1}");

		// Camera panning with P key + mouse movement - pan the viewport
		if(Keyboard.IsKeyDown(Key.P))
		{
			_worldView.SetCurrentValue(CursorProperty, Cursors.SizeAll);

			if(_lastMousePosition.HasValue)
			{
				var deltaX = viewportPoint.X - _lastMousePosition.Value.X;
				var deltaY = viewportPoint.Y - _lastMousePosition.Value.Y;

				Viewmodel.Domain.Viewport.Pan(-deltaX * _cameraPanningSensitivity, -deltaY * _cameraPanningSensitivity);
			}

			_lastMousePosition = viewportPoint;

			return;
		}

		// Camera rotation with R key + mouse movement - always rotate around viewport center
		if(Keyboard.IsKeyDown(Key.R))
		{
			_worldView.SetCurrentValue(CursorProperty, Cursors.Hand);
			Viewmodel.IsRotationGizmoVisible = true;

			if(_lastMousePosition.HasValue)
			{
				var deltaX = viewportPoint.X - _lastMousePosition.Value.X;
				var deltaY = viewportPoint.Y - _lastMousePosition.Value.Y;

				// Snap to 45° increments when Shift is held
				var snap = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

				Viewmodel.Domain.Viewport.RotateCamera(deltaX * _cameraRotationSensitivity, deltaY * _cameraRotationSensitivity, snap);
			}

		_lastMousePosition = viewportPoint;

		return;
	}

	_worldView.SetCurrentValue(CursorProperty, Cursors.Arrow);
	_lastMousePosition = null;
	Viewmodel.IsRotationGizmoVisible = false;

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
				Viewmodel.Domain.AddRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), false);

				return;
			}

			Viewmodel.Domain.AddBody(referencePosition.Value, Viewmodel.CalculateVelocityPerSimulationStep(referencePosition.Value, position));
			Viewmodel.Domain.StopRespawn();

			return;
		}

		if(null == Viewmodel.SelectedBody)
			return;

		// Check if mouse moved significantly in viewport space (2D on screen)
		// Not in world space (3D), since the body could be at any Z depth
		var bodyViewportPoint = Viewmodel.Domain
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
			Viewmodel.Domain.AddRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), true);

			return;
		}

		Viewmodel.Domain.AddOrbitBody(referencePosition.Value, Viewmodel.CalculateVelocityPerSimulationStep(referencePosition.Value, position));
		Viewmodel.Domain.StopRespawn();
	}

	private void OnWorldSizeChanged(object sender, SizeChangedEventArgs args)
		=> Viewmodel.Domain
					.Viewport
					.Resize(new(Viewmodel.Domain.Viewport.ToWorld((float)args.NewSize.Width),
								Viewmodel.Domain.Viewport.ToWorld((float)args.NewSize.Height),
								Viewmodel.Domain.Viewport.ToWorld((float)args.NewSize.Height)));

	private void OnResetClicked(object sender, RoutedEventArgs args)
		=> Viewmodel.Domain
					.Reset();

	private void OnWorldMouseWheel(object sender, MouseWheelEventArgs args)
		=> Viewmodel.Domain
					.Viewport
					.Zoom(Viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender)),
						  -Math.Sign(args.Delta) * (Keyboard.IsKeyDown(Key.LeftAlt)
														? 0.1
														: 1));

	private void OnAutoScaleAndCenterViewportClicked(object sender, RoutedEventArgs args)
		=> Viewmodel.Domain
					.Viewport
					.AutoScaleAndCenter();

	private void OnBodyPresetSelectionChanged(object sender, SelectionChangedEventArgs args)
		=> Viewmodel.IsBodyPresetSelectionVisible = false;

	private void OnEngineTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
		=> Viewmodel.IsEngineSelectionVisible = false;

	private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
	{
	if(e.Key == Key.R)
	{
		Viewmodel.IsRotationGizmoVisible = true;
		e.Handled = true;
		_worldView?.SetCurrentValue(CursorProperty, Cursors.Hand);
	}
	else if(e.Key == Key.P)
	{
		e.Handled = true;
		_worldView?.SetCurrentValue(CursorProperty, Cursors.SizeAll);
	}
}

private void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
{
	if(e.Key == Key.R)
	{
		Viewmodel.IsRotationGizmoVisible = false;
		e.Handled = true;
		_worldView?.SetCurrentValue(CursorProperty, Cursors.Arrow);
	}
	else if(e.Key == Key.P)
	{
		e.Handled = true;
		_worldView?.SetCurrentValue(CursorProperty, Cursors.Arrow);
	}
}

	#endregion
}