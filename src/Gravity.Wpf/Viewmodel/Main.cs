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

public sealed class Main : NotifyPropertyChanged,
						   IMain
{
	#region Fields

	private readonly DispatcherTimer _uiUpdateTimer;
	private const string _stateFileExtension = "grv";

	#endregion

	#region Construction

	internal Main(IApplication application, World world, Viewport viewport)
	{
		Application = application;
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

	public Vector3D CalculateVelocityPerSimulationStep(Vector3D start, Vector3D end)
		=> (end - start) / Application.World.ToWorld(TimeSpan.FromSeconds(1)).Seconds;

	#endregion

	#region Implementation of IMain

	/// <inheritdoc/>
	public IReadOnlyCollection<IApplication.BodyPreset> BodyPresets
		=> Application.BodyPresets;

	/// <inheritdoc/>
	public IReadOnlyCollection<IApplication.EngineType> EngineTypes
		=> Application.EngineTypes;

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

	/// <inheritdoc/>
	IMain.IViewport IMain.Viewport
		=> Viewport;

	/// <inheritdoc/>
	public IApplication Application { get; }

	IMain.IWorld IMain.World
		=> World;

	public required IApplication.BodyPreset SelectedBodyPreset
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Application.Select(value);
		}
	}

	public required IApplication.EngineType SelectedEngineType
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Application.Select(value);
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

			Application.Select(value);
		}
	}

	/// <inheritdoc/>
	public double CpuUtilizationInPercent { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public TimeSpan Runtime { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public int BodyCount { get; set => SetProperty(ref field, value); }

	public bool IsHelpVisible { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public bool IsSimulationRunning
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				Application.StartSimulation();
			else
				Application.StopSimulation();
		}
	}

	public required double SimulationFrequencyInHz
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			Application.SetSimulationFrequency(value);
		}
	} = double.NaN;

	public int FramesPerSecond { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public bool IsBodySelected { get; set => SetProperty(ref field, value); }

	public DragIndicator? DragIndicator { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public ICommand SaveCommand { get; }

	/// <inheritdoc/>
	public ICommand OpenCommand { get; }

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
		FramesPerSecond = (int)Math.Round(1000.0d / Application.FrameDiagnostics.LastMeasurement.LastFrameDurationInMs);
		CpuUtilizationInPercent = Application.FrameDiagnostics.LastMeasurement.CpuUtilizationInPercent;
		Runtime = Application.CurrentRuntime;
		BodyCount = Application.World.CurrentBodyCount;
		IsBodySelected = SelectedBody is not null;
		Viewport.Scale = Application.Viewport.CurrentScale;
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

			await Application.SaveAsync(dlg.FileName);
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

			await Application.OpenAsync(dlg.FileName);
		}
		catch(Exception ex)
		{
			MessageBox.Show($"Fehler beim Öffnen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	#endregion
}