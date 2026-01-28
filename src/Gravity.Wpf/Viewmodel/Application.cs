using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;
using Gravity.Wpf.Commands;
using Microsoft.Win32;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public sealed class Application : NotifyPropertyChanged
{
	#region Fields

	private const string _stateFileExtension = "grv";
	private readonly DispatcherTimer _uiUpdateTimer;

	#endregion

	#region Construction

	internal Application(IApplication application, World world, Viewport viewport)
	{
		Domain = application;
		World = world;
		Viewport = viewport;

		_uiUpdateTimer = new(DispatcherPriority.Render);
		_uiUpdateTimer.Tick += (_, _) => UpdateBindings();

		application.ApplyState += OnApplyState;
		application.UpdateState += OnUpdateState;

		// Initialize commands
		SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync);
		OpenCommand = new AsyncRelayCommand(ExecuteOpenAsync);
	}

	#endregion

	#region Interface

	public Viewport Viewport { get; }

	public World World { get; }

	public IReadOnlyCollection<IApplication.BodyPreset> BodyPresets
		=> Domain.BodyPresets;

	public IReadOnlyCollection<IApplication.EngineType> EngineTypes
		=> Domain.EngineTypes;

	public required double DisplayFrequencyInHz
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_uiUpdateTimer.Interval = TimeSpan.FromSeconds(1.0d / value);
			_uiUpdateTimer.Start();
		}
	} = double.NaN;

	public IApplication Domain { get; }

	public required IApplication.BodyPreset SelectedBodyPreset
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Domain.Select(value);
		}
	}

	public required IApplication.EngineType SelectedEngineType
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Domain.Select(value);
		}
	}

	public bool IsBodyPresetSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool IsEngineSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool IsRotationGizmoVisible { get; set => SetProperty(ref field, value); }

	public required bool ShowPath { get; set => SetProperty(ref field, value); }

	public Body? SelectedBody
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Domain.Select(value);
		}
	}

	public double CpuUtilizationInPercent { get; set => SetProperty(ref field, value); }

	public TimeSpan Runtime { get; set => SetProperty(ref field, value); }

	public int BodyCount { get; set => SetProperty(ref field, value); }

	public bool IsHelpVisible { get; set => SetProperty(ref field, value); }

	public bool IsSimulationRunning
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				Domain.StartSimulation();
			else
				Domain.StopSimulation();
		}
	}

	public required double SimulationFrequencyInHz
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Domain.SetSimulationFrequency(value);
		}
	} = double.NaN;

	public int FramesPerSecond { get; set => SetProperty(ref field, value); }

	public bool IsBodySelected { get; set => SetProperty(ref field, value); }

	public DragIndicator? DragIndicator { get; set => SetProperty(ref field, value); }

	public ICommand SaveCommand { get; }

	public ICommand OpenCommand { get; }

	public Vector3D CalculateVelocityPerSimulationStep(Vector3D start, Vector3D end)
		=> (end - start) / Domain.World.ToWorld(TimeSpan.FromSeconds(1)).Seconds;

	#endregion

	#region Implementation

	private State OnUpdateState(State state)
	{
		state.ShowPath = ShowPath;

		return state;
	}

	private void OnApplyState(State state)
	{
		ShowPath = state.ShowPath;
		Viewport.Autocenter = state.Viewport.Autocenter;
		Viewport.Scale = state.Viewport.Scale;
		World.ElasticCollisions = state.World.ElasticCollisions;
		World.ClosedBoundaries = state.World.ClosedBoundaries;
		World.LogarithmicTimescale = Math.Log10(state.World.Timescale);
	}

	private void UpdateBindings()
	{
		FramesPerSecond = (int)Math.Round(1000.0d / Domain.FrameDiagnostics.LastMeasurement.LastFrameDurationInMs);
		CpuUtilizationInPercent = Domain.FrameDiagnostics.LastMeasurement.CpuUtilizationInPercent;
		Runtime = Domain.CurrentRuntime;
		BodyCount = Domain.World.CurrentBodyCount;
		IsBodySelected = SelectedBody is not null;
		Viewport.Scale = Domain.Viewport.CurrentScale;
		Viewport.CameraYaw = Domain.Viewport.CurrentCameraYaw;
		Viewport.CameraPitch = Domain.Viewport.CurrentCameraPitch;
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level exception handler for user feedback")]
	private async Task ExecuteSaveAsync()
	{
		try
		{
			var dlg = new SaveFileDialog
					  {
						  DefaultExt = _stateFileExtension,
						  Filter = $"Gravity Files | *.{_stateFileExtension}"
					  };
			var dlgResult = dlg.ShowDialog();

			if(dlgResult is not true)
				return;

			await Domain.SaveAsync(dlg.FileName);
		}
		catch(Exception ex)
		{
			MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level exception handler for user feedback")]
	private async Task ExecuteOpenAsync()
	{
		try
		{
			var dlg = new OpenFileDialog
					  {
						  DefaultExt = _stateFileExtension,
						  Filter = $"Gravity Files | *.{_stateFileExtension}"
					  };
			var dlgResult = dlg.ShowDialog();

			if(dlgResult is not true)
				return;

			await Domain.OpenAsync(dlg.FileName);
		}
		catch(Exception ex)
		{
			MessageBox.Show($"Fehler beim Öffnen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	#endregion
}