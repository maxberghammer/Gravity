// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

internal class World : NotifyPropertyChanged,
					   IWorld
{
	#region Internal types

	public sealed class EngineType
	{
		#region Interface

		public Factory.SimulationEngineType Type { get; init; }

		public required string Name { get; init; }

		#endregion
	}

	#endregion

	#region Fields

	private static readonly Guid _randomOrbittingRespawnerId = new("F02C36A4-FEC2-49AD-B3DA-C7E9B6E4C361");
	private static readonly Guid _randomRespawnerId = new("7E4948F8-CFA5-45A3-BB05-48CB4AAB13B1");
	private readonly FrameTiming _frameTiming = new();
	private readonly Dictionary<Guid, Action> _respawnersById = new();
	private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
	private int _isSimulating;
	private ISimulationEngine? _simulationEngine;

	#endregion

	#region Construction

	[SuppressMessage("Usage", "VSTHRD101:Avoid unsupported async delegates", Justification = "<Pending>")]
	public World()
	{
		SelectedEntityPreset = EntityPresets[0];
		SelectedEngineType = EngineTypes[0];

		_respawnersById[_randomRespawnerId] = () => CreateRandomEntities(1, true);
		_respawnersById[_randomOrbittingRespawnerId] = () => CreateRandomOrbitEntities(1, true);

		Entities.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(EntityCount));
		Viewport.PropertyChanged += (_, _) => Updated?.Invoke(this, EventArgs.Empty);

		//var d = new Win32.DEVMODE();

		//Win32.EnumDisplaySettings(null, 0, ref d);

		DisplayFrequency = 60; //d.dmDisplayFrequency;

		_timer.Tick += async (_, _) => await SimulateAsync();
		_timer.Interval = TimeSpan.FromSeconds(1.0d / DisplayFrequency);
		_timer.Start();
	}

	#endregion

	#region Interface

	public event EventHandler? Updated;

	public int DisplayFrequency { get; }

	public double TimeScale { get; set => SetProperty(ref field, value); } = 1;

	public double TimeScaleFactor
		=> Math.Pow(10, TimeScale);

	public EntityPreset[] EntityPresets { get; } =
		[
			EntityPreset.FromDensity("Eisenkugel klein", 7874, 10, Color.DarkGray, Color.White, 2.0d, new("C53FA0C5-AB12-43F7-9548-C098D5C44ADF")),
			EntityPreset.FromDensity("Eisenkugel mittel", 7874, 20, Color.DarkGray, Color.White, 2.0d,
									 new("CB30E40F-FB49-4688-94D1-3F1FB5C3F813")),
			EntityPreset.FromDensity("Eisenkugel groß", 7874, 100, Color.DarkGray, Color.White, 2.0d, new("03F7274E-B6C5-46E8-B8F8-03C969C79B49")),
			new("Mittelschwer+Klein", 100000000000, 20, Color.Green, new("98E60B8E-4461-4895-9107-A1FF5C9B9D64")),
			new("Leicht+Groß", 1000000000, 100, Color.Red, new("B6BBB8AC-109C-4CA1-96E1-976EABED256E")),
			new("Schwer+Groß", 1000000000000, 100, Color.Yellow, new("4F2D1D6B-0ED2-405E-8617-1B5073425F95")),
			new("Schwer+Klein", 1000000000000, 10, Color.Black, Color.White, 2.0d, new("0514F35B-029F-4E91-8071-81FD31C570E0")),
			new("Leicht+Klein", 1000, 20, Color.Blue, new("90424708-FFF6-4BD1-ADAF-6A534BBBACAA")),
			new("Sonne", 1.9884E30d, 696342000.0d, Color.Yellow, new("30584A17-00EE-4B85-ACEB-EFCAF2606468")),
			new("Erde", 5.9724E24d, 12756270.0d / 2, Color.Blue, new("3E9965AB-3A11-414A-A455-50527F254036")),
			new("Mond", 7.346E22d, 3474000.0d / 2, Color.DarkGray, new("71A1DD4C-5B87-405C-8033-B033B46A5237"))
		];

	public EngineType[] EngineTypes { get; } =
		[
			new()
			{
				Type = Factory.SimulationEngineType.Adaptive,
				Name = "Adaptive"
			},
			new()
			{
				Type = Factory.SimulationEngineType.BarnesHutWithLeapfrog,
				Name = "Barnes-Hut mit Leapfrog"
			},
			new()
			{
				Type = Factory.SimulationEngineType.BarnesHutWithRungeKutta,
				Name = "Barnes-Hut mit Runge-Kutta"
			},
			new()
			{
				Type = Factory.SimulationEngineType.Standard,
				Name = "Direkte N-Body"
			},
			new()
			{
				Type = Factory.SimulationEngineType.ClusteredNBody,
				Name = "Clustered N-Body"
			}
		];

	public ObservableCollection<Entity> Entities { get; } = [];

	public EntityPreset SelectedEntityPreset { get; set => SetProperty(ref field, value); }

	public EngineType SelectedEngineType
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_simulationEngine = Factory.Create(value.Type);
		}
	}

	public bool IsEntityPresetSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool IsEngineSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool ShowPath { get; set => SetProperty(ref field, value); } = true;

	public Viewport Viewport { get; } = new();

	public TimeSpan RuntimeInSeconds { get; private set; }

	public int CpuUtilizationInPercent { get; private set; }

	public int EntityCount
		=> Entities.Count;

	public bool AutoCenterViewport { get; set => SetProperty(ref field, value); }

	public Entity? SelectedEntity { get; set => SetProperty(ref field, value); }

	public bool IsRunning { get; set => SetProperty(ref field, value); } = true;

	public Guid? CurrentRespawnerId { get; set; }

	public bool IsHelpVisible { get; set => SetProperty(ref field, value); }

	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
	public void CreateRandomEntities(int count, bool enableRespawn)
	{
		var rnd = new Random();
		var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

		for(var i = 0; i < count; i++)
		{
			var position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

			var entities = Entities.ToArrayLocked();

			while(entities.Any(e => (e.Position - position).Length <= e.r + SelectedEntityPreset.r))
				position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;
			CreateEntity(position, Vector2D.Zero);
			CurrentRespawnerId = enableRespawn
									 ? _randomRespawnerId
									 : null;
		}
	}

	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
	public void CreateRandomOrbitEntities(int count, bool enableRespawn)
	{
		var rnd = new Random();
		var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

		for(var i = 0; i < count; i++)
		{
			var position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;
			var entities = Entities.ToArrayLocked();

			while(entities.Any(e => (e.Position - position).Length <= e.r + SelectedEntityPreset.r))
				position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;
			CreateOrbitEntity(position, Vector2D.Zero);
			CurrentRespawnerId = enableRespawn
									 ? _randomOrbittingRespawnerId
									 : null;
		}
	}

	public void CreateEntity(Vector2D position, Vector2D velocity)
		=> Entities.AddLocked(new(position,
								  SelectedEntityPreset.r,
								  SelectedEntityPreset.m,
								  velocity,
								  Vector2D.Zero,
								  this, SelectedEntityPreset.Fill, SelectedEntityPreset.Stroke, SelectedEntityPreset.StrokeWidth));

	public void CreateOrbitEntity(Vector2D position, Vector2D velocity)
	{
		var nearestEntity = SelectedEntity
							?? Entities.ToArrayLocked()
									   .OrderByDescending(p => IWorld.G * p.m / ((p.Position - position).Length * (p.Position - position).Length))
									   .FirstOrDefault();

		if(null == nearestEntity)
		{
			CreateEntity(position, Vector2D.Zero);

			return;
		}

		var dist = position - nearestEntity.Position;
		var direction = (dist.Norm().Unit() - velocity.Unit()).Length > (-dist.Norm().Unit() - velocity.Unit()).Length
							? -1
							: 1;
		var g = IWorld.G * (SelectedEntityPreset.m * nearestEntity.m) / dist.LengthSquared * -dist.Unit();
		var v = (1 + velocity.Length) * direction * Math.Sqrt(g.Length / SelectedEntityPreset.m * dist.Length) * dist.Norm().Unit() + nearestEntity.v;
		CreateEntity(position, v);
	}

	public void SelectEntity(Point viewportPoint, double viewportSearchRadius)
	{
		var pos = Viewport.ToWorld(viewportPoint);
		SelectedEntity = Entities.ToArrayLocked()
								 .Where(e => (e.Position - pos).Length <= e.r + viewportSearchRadius / Viewport.ScaleFactor)
								 .OrderBy(e => (e.Position - pos).Length - (e.r + viewportSearchRadius / Viewport.ScaleFactor))
								 .FirstOrDefault();
	}

	public void AutoScaleAndCenterViewport()
	{
		var entities = Entities.ToArrayLocked();

		if(entities.Length == 0)
			return;

		var previousSize = Viewport.Size;
		var topLeft = new Vector2D(entities.Min(e => e.Position.X - e.r), entities.Min(e => e.Position.Y - e.r));
		var bottomRight = new Vector2D(entities.Max(e => e.Position.X + e.r), entities.Max(e => e.Position.Y + e.r));
		var center = topLeft + (bottomRight - topLeft) / 2;
		var newSize = bottomRight - topLeft;
		if(newSize.X / newSize.Y < previousSize.X / previousSize.Y)
			newSize.X = newSize.Y * previousSize.X / previousSize.Y;
		if(newSize.X / newSize.Y > previousSize.X / previousSize.Y)
			newSize.Y = newSize.X * previousSize.Y / previousSize.X;
		Viewport.TopLeft = center - newSize / 2;
		Viewport.BottomRight = center + newSize / 2;
		Viewport.Scale += Math.Log10(Math.Max(newSize.X / previousSize.X, newSize.Y / previousSize.Y));
	}

	public void Reset()
	{
		Entities.ClearLocked();
		RuntimeInSeconds = TimeSpan.Zero;
		SelectedEntity = null;
		var viewportSize = Viewport.Size;
		Viewport.TopLeft = -viewportSize / 2;
		Viewport.BottomRight = viewportSize / 2;
	}

	public async Task SaveAsync(string filePath)
	{
		var state = new State
					{
						Viewport = new(new(Viewport.TopLeft.X, Viewport.TopLeft.Y),
									   new(Viewport.BottomRight.X, Viewport.BottomRight.Y),
									   Viewport.Scale),
						AutoCenterViewport = AutoCenterViewport,
						ClosedBoundaries = ClosedBoundaries,
						ElasticCollisions = ElasticCollisions,
						ShowPath = ShowPath,
						TimeScale = TimeScale,
						SelectedEntityPresetId = SelectedEntityPreset.Id,
						RespawnerId = CurrentRespawnerId,
						Entities = Entities.ToArrayLocked()
										   .Select(e => new State.EntityState(e.Fill.ToString(), e.Stroke?.ToString(),
																			  e.StrokeWidth,
																			  new(e.Position.X, e.Position.Y),
																			  new(e.v.X, e.v.Y),
																			  e.r,
																			  e.m))
										   .ToArray()
					};
		await using var swr = File.CreateText(filePath);
		await state.SerializeAsync(swr);
	}

	public async Task OpenAsync(string filePath)
	{
		using var srd = File.OpenText(filePath);
		var state = await State.DeserializeAsync(srd);

		Reset();

		Viewport.TopLeft = new(state.Viewport.TopLeft.X, state.Viewport.TopLeft.Y);
		Viewport.BottomRight = new(state.Viewport.BottomRight.X, state.Viewport.BottomRight.Y);
		Viewport.Scale = state.Viewport.Scale;
		AutoCenterViewport = state.AutoCenterViewport;
		ClosedBoundaries = state.ClosedBoundaries;
		ElasticCollisions = state.ElasticCollisions;
		SelectedEntityPreset = EntityPresets.First(p => p.Id == state.SelectedEntityPresetId);
		CurrentRespawnerId = state.RespawnerId;
		ShowPath = state.ShowPath;
		TimeScale = state.TimeScale;

		Entities.AddRangeLocked(state.Entities
									 .Select(e => new Entity(new(e.Position.X, e.Position.Y),
															 e.r,
															 e.m,
															 new(e.v.X, e.v.Y),
															 Vector2D.Zero,
															 this,
															 string.IsNullOrEmpty(e.FillColor)
																 ? Color.Transparent
																 : Color.Parse(e.FillColor),
															 string.IsNullOrEmpty(e.StrokeColor)
																 ? null
																 : Color.Parse(e.StrokeColor),
															 e.StrokeWidth)));
	}

	#endregion

	#region Implementation of IWorld

	public bool ElasticCollisions { get; set => SetProperty(ref field, value); } = true;

	public bool ClosedBoundaries { get; set => SetProperty(ref field, value); } = true;

	IViewport IWorld.Viewport
		=> Viewport;

	#endregion

	#region Implementation

	private async Task SimulateAsync()
		=> await Task.Run(Simulate);

	private void Simulate()
	{
		if(1 == Interlocked.CompareExchange(ref _isSimulating, 1, 0))
			return;

		using(_frameTiming.Measure())
		{
			var deltaTime = TimeSpan.FromSeconds(1.0d / DisplayFrequency * TimeScaleFactor);

			if(IsRunning)
			{
				UpdateAllEntities(deltaTime);

				if(AutoCenterViewport)
					DoAutoCenterViewport();

				RuntimeInSeconds += deltaTime;
			}
		}

		var frameTimingMeasurement = _frameTiming.LastMeasurement;

		CpuUtilizationInPercent = (int)Math.Round(frameTimingMeasurement.CpuUtilizationEmaInPercent);

		// Log every ~60 frames; use TraceInformation for explicit event type
		if(frameTimingMeasurement.FrameCount % 60 == 0)
			Trace.TraceInformation($"Frame: {frameTimingMeasurement.LastFrameDurationInMs:F1} ms, CPU: {frameTimingMeasurement.CpuUtilizationInPercent}% | Entities: {EntityCount}");

		Updated?.Invoke(this, EventArgs.Empty);

		_isSimulating = 0;
	}

	private void UpdateAllEntities(TimeSpan deltaTime)
	{
		var entities = Entities.ToArrayLocked();

		if(entities.Length == 0 ||
		   _simulationEngine == null)
			return;

		_simulationEngine.Simulate(entities, deltaTime);

		var respawner = CurrentRespawnerId.HasValue
							? _respawnersById[CurrentRespawnerId.Value]
							: null;

		var absorbedEntities = entities.Where(e => e.IsAbsorbed).ToArray();

		// Absorbierte Objekte entfernen
		Entities.RemoveRangeLocked(absorbedEntities);

		// Bei Bedarf Respawner aufrufen
		if(null == respawner)
			return;

		foreach(var _ in absorbedEntities)
			respawner();
	}

	private void DoAutoCenterViewport()
	{
		var entities = Entities.ToArrayLocked();

		if(entities.Length == 0)
			return;

		var previousSize = Viewport.Size;
		var topLeft = new Vector2D(entities.Min(e => e.Position.X - e.r),
								   entities.Min(e => e.Position.Y - e.r));
		var bottomRight = new Vector2D(entities.Max(e => e.Position.X + e.r),
									   entities.Max(e => e.Position.Y + e.r));
		var center = topLeft + (bottomRight - topLeft) / 2;

		Viewport.TopLeft = center - previousSize / 2;
		Viewport.BottomRight = center + previousSize / 2;
	}

	#endregion
}