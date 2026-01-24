// Erstellt am: 23.01.2026
// Erstellt von: MaxBerghammer

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;

namespace Gravity.Wpf.Viewmodel;

public interface IMain : INotifyPropertyChanged
{
	#region Internal types

	public interface IViewport : INotifyPropertyChanged
	{
		bool Autocenter { get; set; }

		double Scale { get; set; }
	}

	public interface IWorld : INotifyPropertyChanged
	{
		bool ClosedBoundaries { get; set; }

		bool ElasticCollisions { get; set; }

		double LogarithmicTimescale { get; set; }
	}

	#endregion

	IReadOnlyCollection<IApplication.BodyPreset> BodyPresets { get; }

	IReadOnlyCollection<IApplication.EngineType> EngineTypes { get; }

	public double DisplayFrequencyInHz { get; set; }

	IApplication Application { get; }

	IWorld World { get; }

	IViewport Viewport { get; }

	IApplication.BodyPreset SelectedBodyPreset { get; set; }

	IApplication.EngineType SelectedEngineType { get; set; }

	bool IsBodyPresetSelectionVisible { get; set; }

	bool IsEngineSelectionVisible { get; set; }

	bool ShowPath { get; set; }

	Body? SelectedBody { get; set; }

	double CpuUtilizationInPercent { get; }

	TimeSpan Runtime { get; }

	int BodyCount { get; }

	bool IsHelpVisible { get; set; }

	bool IsSimulationRunning { get; set; }

	double SimulationFrequencyInHz { get; set; }

	int FramesPerSecond { get; set; }

	bool IsBodySelected { get; set; }

	DragIndicator? DragIndicator { get; set; }
}