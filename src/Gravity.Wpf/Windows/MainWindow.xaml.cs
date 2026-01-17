using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Gravity.SimulationEngine;
using Microsoft.Win32;

namespace Gravity.Wpf.Windows;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed partial class MainWindow
{
	#region Fields

	private const string _stateFileExtension = "grv";
	private const int _viewportSelectionSearchRadius = 30;
	private const double _cameraRotationSensitivity = 0.005;
	private static Vector3D? _referencePosition;
	private Point? _lastMousePosition;

	// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
	private readonly DispatcherTimer _uiUpdateTimer;
	private bool _wasRunning;

	#endregion

	#region Construction

	public MainWindow()
	{
		InitializeComponent();

		_uiUpdateTimer = new(DispatcherPriority.Render)
						 {
							 Interval = TimeSpan.FromSeconds(1.0d / _viewmodel.DisplayFrequency),
							 IsEnabled = true
						 };

		_uiUpdateTimer.Tick += OnUpdateUi;
	}

	#endregion

	#region Implementation

	private void OnUpdateUi(object? sender, EventArgs args)
	{
		_lblSelectedBodym.Visibility = _lblSelectedBodyv.Visibility = null != _viewmodel.SelectedBody
																		  ? Visibility.Visible
																		  : Visibility.Collapsed;
		_lblSelectedBodyv.Content = _viewmodel.SelectedBody?.v;
		_lblSelectedBodym.Content = _viewmodel.SelectedBody?.m;
		_lblFps.Content = _viewmodel.FramesPerSecond;
		_lblCpuUtilizationInPercent.Content = _viewmodel.CpuUtilizationInPercent;
		_lblRuntime.Content = _viewmodel.Runtime;
		_lblBodyCount.Content = _viewmodel.BodyCount;
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseDown(object sender, MouseButtonEventArgs args)
	{
	var viewportPoint = args.GetPosition((IInputElement)sender);

	_wasRunning = _viewmodel.IsRunning;
		_viewmodel.IsRunning = false;

		if(Keyboard.Modifiers == ModifierKeys.None &&
		   args.LeftButton == MouseButtonState.Pressed)
		{
			_viewmodel.SelectBody(viewportPoint, _viewportSelectionSearchRadius);

			if(null != _viewmodel.SelectedBody)
			{
				var entityViewportPoint = _viewmodel.Viewport.ToViewport(_viewmodel.SelectedBody.Position);

				_viewmodel.Viewport.DragIndicator = new()
													{
														Start = new(entityViewportPoint.X, entityViewportPoint.Y),
														End = new(entityViewportPoint.X, entityViewportPoint.Y),
														Diameter = (_viewmodel.SelectedBody.r + _viewmodel.SelectedBody.AtmosphereThickness) * 2 * _viewmodel.Viewport.ScaleFactor
													};

				return;
			}
		}

		_referencePosition = _viewmodel.Viewport.ToWorld(viewportPoint);
		_viewmodel.Viewport.DragIndicator = new()
											{
												Start = new(viewportPoint.X, viewportPoint.Y),
												End = new(viewportPoint.X, viewportPoint.Y),
												Diameter = (_viewmodel.SelectedBodyPreset.r + _viewmodel.SelectedBodyPreset.AtmosphereThickness) * 2 *
														   _viewmodel.Viewport.ScaleFactor
											};
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseUp(object sender, MouseButtonEventArgs args)
	{
	// Reset last mouse position when any button is released
	_lastMousePosition = null;
	}

	private void OnWorldMouseMove(object sender, MouseEventArgs args)
	{
	var viewportPoint = args.GetPosition((IInputElement)sender);

	// Camera rotation with R key + mouse movement - always rotate around viewport center
	if(Keyboard.IsKeyDown(Key.R))
	{
	if(_lastMousePosition.HasValue)
	{
	var deltaX = viewportPoint.X - _lastMousePosition.Value.X;
	var deltaY = viewportPoint.Y - _lastMousePosition.Value.Y;
	_viewmodel.Viewport.RotateCamera(deltaX * _cameraRotationSensitivity, deltaY * _cameraRotationSensitivity);
	}
	_lastMousePosition = viewportPoint;
	return;
	}
	else
	{
	_lastMousePosition = null;
	}

		if(null == _viewmodel.Viewport.DragIndicator)
			return;

		_viewmodel.Viewport.DragIndicator.End = new(viewportPoint.X, viewportPoint.Y);

		var position = _viewmodel.Viewport.ToWorld(viewportPoint);

		if(_referencePosition.HasValue)
		{
			_viewmodel.Viewport.DragIndicator.Label = args.RightButton == MouseButtonState.Pressed
														  ? $"Δv={(position - _referencePosition.Value) / _viewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - _referencePosition.Value) / _viewmodel.TimeScaleFactor}m/s";

			return;
		}

		if(null != _viewmodel.SelectedBody)
			_viewmodel.Viewport.DragIndicator.Label = Keyboard.IsKeyDown(Key.LeftAlt)
														  ? $"Δv={(position - _viewmodel.SelectedBody.Position) / _viewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - _viewmodel.SelectedBody.Position) / _viewmodel.TimeScaleFactor}m/s";
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
	{
		_viewmodel.IsRunning = _wasRunning;

		var referencePosition = _referencePosition;
		var position = _viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		_viewmodel.Viewport.DragIndicator = null;
		_referencePosition = null;

		if(null != referencePosition)
		{
			if(Keyboard.IsKeyDown(Key.LeftAlt))
			{
				_viewmodel.CreateRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), false);

				return;
			}

			_viewmodel.CreateBody(referencePosition.Value, (position - referencePosition.Value) / _viewmodel.TimeScaleFactor);
			_viewmodel.CurrentRespawnerId = null;

			return;
		}

		if(null != _viewmodel.SelectedBody)
		{
			if((position - _viewmodel.SelectedBody.Position).Length <=
			   _viewmodel.SelectedBody.r + _viewportSelectionSearchRadius / _viewmodel.Viewport.ScaleFactor)
				return;

			if(Keyboard.IsKeyDown(Key.LeftAlt))
				_viewmodel.SelectedBody.v += (position - _viewmodel.SelectedBody.Position) / _viewmodel.TimeScaleFactor;
			else
				_viewmodel.SelectedBody.v = (position - _viewmodel.SelectedBody.Position) / _viewmodel.TimeScaleFactor;
		}
	}

	[SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
	private void OnWorldRightButtonUp(object sender, MouseButtonEventArgs args)
	{
		_viewmodel.IsRunning = _wasRunning;

		var referencePosition = _referencePosition;
		var position = _viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		_viewmodel.Viewport.DragIndicator = null;
		_referencePosition = null;

		if(null != referencePosition)
		{
			if(Keyboard.IsKeyDown(Key.LeftAlt))
			{
				_viewmodel.CreateRandomBodies(100, Keyboard.IsKeyDown(Key.LeftShift), true);

				return;
			}

			_viewmodel.CreateOrbitBody(referencePosition.Value, (position - referencePosition.Value) / _viewmodel.TimeScaleFactor);
			_viewmodel.CurrentRespawnerId = null;
		}
	}

	private void OnWorldSizeChanged(object sender, SizeChangedEventArgs args)
	{
		var center = _viewmodel.Viewport.Center;
		var newSize = new Vector3D(args.NewSize.Width, args.NewSize.Height, _viewmodel.Viewport.Depth) / _viewmodel.Viewport.ScaleFactor;

		_viewmodel.Viewport.SetBoundsAroundCenter(center, newSize);
	}

	private void OnResetClicked(object sender, RoutedEventArgs args)
		=> _viewmodel.Reset();

	private void OnWorldMouseWheel(object sender, MouseWheelEventArgs args)
		=> _viewmodel.Viewport.Zoom(_viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender)),
									-Math.Sign(args.Delta) * (Keyboard.IsKeyDown(Key.LeftAlt)
																  ? 0.1
																  : 1));

	private void OnAutoScaleAndCenterViewportClicked(object sender, RoutedEventArgs args)
		=> _viewmodel.AutoScaleAndCenterViewport();

	private void OnBodyPresetSelectionChanged(object sender, SelectionChangedEventArgs args)
		=> _viewmodel.IsBodyPresetSelectionVisible = false;

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

		await _viewmodel.SaveAsync(dlg.FileName);
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

		await _viewmodel.OpenAsync(dlg.FileName);
	}

	private void OnEngineTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
		=> _viewmodel.IsEngineSelectionVisible = false;

	#endregion
}