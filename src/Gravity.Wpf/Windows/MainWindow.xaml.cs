using Gravity.SimulationEngine;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wellenlib;

namespace Gravity.Wpf.Windows;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed partial class MainWindow
{
	#region Fields

	private const string _stateFileExtension = "grv";
	private const int _viewportSelectionSearchRadius = 30;
	private static Vector2D? _referencePosition;

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
		_lblSelectedEntitym.Visibility = _lblSelectedEntityv.Visibility = null != _viewmodel.SelectedEntity
																			  ? Visibility.Visible
																			  : Visibility.Collapsed;
		_lblSelectedEntityv.Content = _viewmodel.SelectedEntity?.v;
		_lblSelectedEntitym.Content = _viewmodel.SelectedEntity?.m;
		_lblCpuUtilizationInPercent.Content = _viewmodel.CpuUtilizationInPercent;
		_lblRuntimeInSeconds.Content = _viewmodel.RuntimeInSeconds;
	}

    [SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
    private void OnWorldMouseDown(object sender, MouseButtonEventArgs args)
	{
		var viewportPoint = args.GetPosition((IInputElement)sender);

		_wasRunning = _viewmodel.IsRunning;
		_viewmodel.IsRunning = false;

		if (Keyboard.Modifiers == ModifierKeys.None &&
		   args.LeftButton == MouseButtonState.Pressed)
		{
			_viewmodel.SelectEntity(viewportPoint, _viewportSelectionSearchRadius);

			if (null != _viewmodel.SelectedEntity)
			{
				var entityViewportPoint = _viewmodel.Viewport.ToViewport(_viewmodel.SelectedEntity.Position);

				_viewmodel.Viewport.DragIndicator = new()
				{
					Start = new(entityViewportPoint.X, entityViewportPoint.Y),
					End = new(entityViewportPoint.X, entityViewportPoint.Y),
					Diameter = (_viewmodel.SelectedEntity.r + _viewmodel.SelectedEntity.StrokeWidth) * 2 * _viewmodel.Viewport.ScaleFactor
				};

				return;
			}
		}

		_referencePosition = _viewmodel.Viewport.ToWorld(viewportPoint);
		_viewmodel.Viewport.DragIndicator = new()
		{
			Start = new(viewportPoint.X, viewportPoint.Y),
			End = new(viewportPoint.X, viewportPoint.Y),
			Diameter = (_viewmodel.SelectedEntityPreset.r + _viewmodel.SelectedEntityPreset.StrokeWidth) * 2 *
														   _viewmodel.Viewport.ScaleFactor
		};
	}

	private void OnWorldMouseMove(object sender, MouseEventArgs args)
	{
		if (null == _viewmodel.Viewport.DragIndicator)
			return;

		var viewportPoint = args.GetPosition((IInputElement)sender);

		_viewmodel.Viewport.DragIndicator.End = new(viewportPoint.X, viewportPoint.Y);

		var position = _viewmodel.Viewport.ToWorld(viewportPoint);

		if (_referencePosition.HasValue)
		{
			_viewmodel.Viewport.DragIndicator.Label = args.RightButton == MouseButtonState.Pressed
														  ? $"Δv={(position - _referencePosition.Value) / _viewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - _referencePosition.Value) / _viewmodel.TimeScaleFactor}m/s";

			return;
		}

		if (null != _viewmodel.SelectedEntity)
			_viewmodel.Viewport.DragIndicator.Label = Keyboard.IsKeyDown(Key.LeftAlt)
														  ? $"Δv={(position - _viewmodel.SelectedEntity.Position) / _viewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - _viewmodel.SelectedEntity.Position) / _viewmodel.TimeScaleFactor}m/s";
	}

    [SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
    private void OnWorldMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
	{
		_viewmodel.IsRunning = _wasRunning;

		var referencePosition = _referencePosition;
		var position = _viewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		_viewmodel.Viewport.DragIndicator = null;
		_referencePosition = null;

		if (null != referencePosition)
		{
			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				_viewmodel.CreateRandomEntities(100, Keyboard.IsKeyDown(Key.LeftShift));

				return;
			}

			_viewmodel.CreateEntity(referencePosition.Value, (position - referencePosition.Value) / _viewmodel.TimeScaleFactor);
			_viewmodel.CurrentRespawnerId = null;

			return;
		}

		if (null != _viewmodel.SelectedEntity)
		{
			if ((position - _viewmodel.SelectedEntity.Position).Length <=
			   _viewmodel.SelectedEntity.r + _viewportSelectionSearchRadius / _viewmodel.Viewport.ScaleFactor)
				return;

			if (Keyboard.IsKeyDown(Key.LeftAlt))
				_viewmodel.SelectedEntity.v += (position - _viewmodel.SelectedEntity.Position) / _viewmodel.TimeScaleFactor;
			else
				_viewmodel.SelectedEntity.v = (position - _viewmodel.SelectedEntity.Position) / _viewmodel.TimeScaleFactor;
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

		if (null != referencePosition)
		{
			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				_viewmodel.CreateRandomOrbitEntities(100, Keyboard.IsKeyDown(Key.LeftShift));

				return;
			}

			_viewmodel.CreateOrbitEntity(referencePosition.Value, (position - referencePosition.Value) / _viewmodel.TimeScaleFactor);
			_viewmodel.CurrentRespawnerId = null;
		}
	}

	private void OnWorldSizeChanged(object sender, SizeChangedEventArgs args)
	{
		var center = _viewmodel.Viewport.Center;
		var newSize = new Vector2D(args.NewSize.Width, args.NewSize.Height);

		_viewmodel.Viewport.TopLeft = center - newSize / 2 / _viewmodel.Viewport.ScaleFactor;
		_viewmodel.Viewport.BottomRight = center + newSize / 2 / _viewmodel.Viewport.ScaleFactor;
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

	private void OnEntityPresetSelectionChanged(object sender, SelectionChangedEventArgs args)
		=> _viewmodel.IsEntityPresetSelectionVisible = false;

    [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
    private async void OnSaveClicked(object sender, RoutedEventArgs args)
	{
		var dlg = new SaveFileDialog
		{
			DefaultExt = _stateFileExtension,
			Filter = $"Gravity Files | *.{_stateFileExtension}"
		};
		var dlgResult = dlg.ShowDialog(this);

		if (dlgResult is not true)
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

	#endregion
}