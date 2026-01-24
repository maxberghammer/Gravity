using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public sealed class Main : NotifyPropertyChanged,
						   IMain
{
	#region Fields

	private readonly DispatcherTimer _uiUpdateTimer;

	#endregion

	#region Construction

	internal Main(IApplication application, World world, Viewport viewport)
	{
		Application = application;
		World = world;
		Viewport = viewport;

		_uiUpdateTimer = new(DispatcherPriority.Render);
		_uiUpdateTimer.Tick += (_, _) => UpdateBindings();
	}

	#endregion

	#region Interface

	public Viewport Viewport { get; }

	public World World { get; }

	public Vector3D CalculateVelocityPerSimulationStep(Vector3D start, Vector3D end)
		=> (end - start) / Application.World.ToWorld(TimeSpan.FromSeconds(1)).Seconds;

	#endregion

	#region Implementation of IMain

	/// <inheritdoc />
	public IReadOnlyCollection<IApplication.BodyPreset> BodyPresets
		=> Application.BodyPresets;

	/// <inheritdoc />
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
	}

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
	}

	public int FramesPerSecond { get; set => SetProperty(ref field, value); }

	/// <inheritdoc/>
	public bool IsBodySelected { get; set => SetProperty(ref field, value); }

	public DragIndicator? DragIndicator { get; set => SetProperty(ref field, value); }

	#endregion

	#region Implementation

	private void UpdateBindings()
	{
		FramesPerSecond = (int)Math.Round(1000.0d / Application.FrameDiagnostics.LastMeasurement.LastFrameDurationInMs);
		CpuUtilizationInPercent = Application.FrameDiagnostics.LastMeasurement.CpuUtilizationInPercent;
		Runtime = Application.Runtime;
		BodyCount = Application.World.BodyCount;
		IsBodySelected = SelectedBody is not null;
	}

	#endregion
}