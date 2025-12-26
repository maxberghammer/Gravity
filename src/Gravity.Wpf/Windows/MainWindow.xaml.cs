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
			Interval = TimeSpan.FromSeconds(1.0d / mViewmodel.DisplayFrequency),
			IsEnabled = true
		};

		_uiUpdateTimer.Tick += OnUpdateUi;
	}

	#endregion

	#region Implementation

	private void OnUpdateUi(object? sender, EventArgs args)
	{
		mLblSelectedEntitym.Visibility = mLblSelectedEntityv.Visibility = null != mViewmodel.SelectedEntity
																			  ? Visibility.Visible
																			  : Visibility.Collapsed;
		mLblSelectedEntityv.Content = mViewmodel.SelectedEntity?.v;
		mLblSelectedEntitym.Content = mViewmodel.SelectedEntity?.m;
		mLblCpuUtilizationInPercent.Content = mViewmodel.CpuUtilizationInPercent;
		mLblRuntimeInSeconds.Content = mViewmodel.RuntimeInSeconds;
	}

    [SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
    private void OnWorldMouseDown(object sender, MouseButtonEventArgs args)
	{
		var viewportPoint = args.GetPosition((IInputElement)sender);

		_wasRunning = mViewmodel.IsRunning;
		mViewmodel.IsRunning = false;

		if (Keyboard.Modifiers == ModifierKeys.None &&
		   args.LeftButton == MouseButtonState.Pressed)
		{
			mViewmodel.SelectEntity(viewportPoint, _viewportSelectionSearchRadius);

			if (null != mViewmodel.SelectedEntity)
			{
				var entityViewportPoint = mViewmodel.Viewport.ToViewport(mViewmodel.SelectedEntity.Position);

				mViewmodel.Viewport.DragIndicator = new()
				{
					Start = new(entityViewportPoint.X, entityViewportPoint.Y),
					End = new(entityViewportPoint.X, entityViewportPoint.Y),
					Diameter = (mViewmodel.SelectedEntity.r + mViewmodel.SelectedEntity.StrokeWidth) * 2 * mViewmodel.Viewport.ScaleFactor
				};

				return;
			}
		}

		_referencePosition = mViewmodel.Viewport.ToWorld(viewportPoint);
		mViewmodel.Viewport.DragIndicator = new()
		{
			Start = new(viewportPoint.X, viewportPoint.Y),
			End = new(viewportPoint.X, viewportPoint.Y),
			Diameter = (mViewmodel.SelectedEntityPreset.r + mViewmodel.SelectedEntityPreset.StrokeWidth) * 2 *
														   mViewmodel.Viewport.ScaleFactor
		};
	}

	private void OnWorldMouseMove(object sender, MouseEventArgs args)
	{
		if (null == mViewmodel.Viewport.DragIndicator)
			return;

		var viewportPoint = args.GetPosition((IInputElement)sender);

		mViewmodel.Viewport.DragIndicator.End = new(viewportPoint.X, viewportPoint.Y);

		var position = mViewmodel.Viewport.ToWorld(viewportPoint);

		if (_referencePosition.HasValue)
		{
			mViewmodel.Viewport.DragIndicator.Label = args.RightButton == MouseButtonState.Pressed
														  ? $"Δv={(position - _referencePosition.Value) / mViewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - _referencePosition.Value) / mViewmodel.TimeScaleFactor}m/s";

			return;
		}

		if (null != mViewmodel.SelectedEntity)
			mViewmodel.Viewport.DragIndicator.Label = Keyboard.IsKeyDown(Key.LeftAlt)
														  ? $"Δv={(position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor}m/s"
														  : $"v={(position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor}m/s";
	}

    [SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
    private void OnWorldMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
	{
		mViewmodel.IsRunning = _wasRunning;

		var referencePosition = _referencePosition;
		var position = mViewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		mViewmodel.Viewport.DragIndicator = null;
		_referencePosition = null;

		if (null != referencePosition)
		{
			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				mViewmodel.CreateRandomEntities(100, Keyboard.IsKeyDown(Key.LeftShift));

				return;
			}

			mViewmodel.CreateEntity(referencePosition.Value, (position - referencePosition.Value) / mViewmodel.TimeScaleFactor);
			mViewmodel.CurrentRespawnerId = null;

			return;
		}

		if (null != mViewmodel.SelectedEntity)
		{
			if ((position - mViewmodel.SelectedEntity.Position).Length <=
			   mViewmodel.SelectedEntity.r + _viewportSelectionSearchRadius / mViewmodel.Viewport.ScaleFactor)
				return;

			if (Keyboard.IsKeyDown(Key.LeftAlt))
				mViewmodel.SelectedEntity.v += (position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor;
			else
				mViewmodel.SelectedEntity.v = (position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor;
		}
	}

    [SuppressMessage("Critical Code Smell", "S2696:Instance members should not write to \"static\" fields", Justification = "<Pending>")]
    private void OnWorldRightButtonUp(object sender, MouseButtonEventArgs args)
	{
		mViewmodel.IsRunning = _wasRunning;

		var referencePosition = _referencePosition;
		var position = mViewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender));

		mViewmodel.Viewport.DragIndicator = null;
		_referencePosition = null;

		if (null != referencePosition)
		{
			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				mViewmodel.CreateRandomOrbitEntities(100, Keyboard.IsKeyDown(Key.LeftShift));

				return;
			}

			mViewmodel.CreateOrbitEntity(referencePosition.Value, (position - referencePosition.Value) / mViewmodel.TimeScaleFactor);
			mViewmodel.CurrentRespawnerId = null;
		}
	}

	private void OnWorldSizeChanged(object sender, SizeChangedEventArgs args)
	{
		var center = mViewmodel.Viewport.Center;
		var newSize = new Vector2D(args.NewSize.Width, args.NewSize.Height);

		mViewmodel.Viewport.TopLeft = center - newSize / 2 / mViewmodel.Viewport.ScaleFactor;
		mViewmodel.Viewport.BottomRight = center + newSize / 2 / mViewmodel.Viewport.ScaleFactor;
	}

	private void OnResetClicked(object sender, RoutedEventArgs args)
		=> mViewmodel.Reset();

	private void OnWorldMouseWheel(object sender, MouseWheelEventArgs args)
		=> mViewmodel.Viewport.Zoom(mViewmodel.Viewport.ToWorld(args.GetPosition((IInputElement)sender)),
									-Math.Sign(args.Delta) * (Keyboard.IsKeyDown(Key.LeftAlt)
																  ? 0.1
																  : 1));

	private void OnAutoScaleAndCenterViewportClicked(object sender, RoutedEventArgs args)
		=> mViewmodel.AutoScaleAndCenterViewport();

	private void OnEntityPresetSelectionChanged(object sender, SelectionChangedEventArgs args)
		=> mViewmodel.IsEntityPresetSelectionVisible = false;

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

		await mViewmodel.SaveAsync(dlg.FileName);
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

		await mViewmodel.OpenAsync(dlg.FileName);
	}

	#endregion
}