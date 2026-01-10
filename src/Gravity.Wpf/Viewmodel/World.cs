// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Gravity.SimulationEngine;
using SharpGL;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

internal class World : NotifyPropertyChanged,
					   IWorld
{
	#region Internal types

	private sealed class State
	{
		#region Internal types

		public class ViewportState
		{
			#region Interface

			public Vector2D TopLeft { get; init; }

			public Vector2D BottomRight { get; init; }

			public double Scale { get; init; }

			#endregion
		}

		public class EntityState
		{
			#region Interface

			public required string FillColor { get; init; }

			public required string StrokeColor { get; init; }

			public double StrokeWidth { get; init; }

			public Vector2D Position { get; init; }

			// ReSharper disable once InconsistentNaming
			[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
			public Vector2D v { get; init; }

			// ReSharper disable once InconsistentNaming
			[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
			public double r { get; init; }

			// ReSharper disable once InconsistentNaming
			[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
			public double m { get; init; }

			#endregion
		}

		#endregion

		#region Interface

		public required ViewportState Viewport { get; init; }

		public double TimeScale { get; init; }

		public bool ElasticCollisions { get; init; }

		public bool ClosedBoundaries { get; init; }

		public bool ShowPath { get; init; }

		public bool AutoCenterViewport { get; init; }

		public Guid SelectedEntityPresetId { get; init; }

		public Guid? RespawnerId { get; init; }

		public required EntityState[] Entities { get; init; }

		#endregion
	}

	#endregion

	#region Fields

	public static readonly double G = Math.Pow(6.67430d, -11.0);
	private static readonly Guid _randomOrbittingRespawnerId = new("F02C36A4-FEC2-49AD-B3DA-C7E9B6E4C361");
	private static readonly Guid _randomRespawnerId = new("7E4948F8-CFA5-45A3-BB05-48CB4AAB13B1");
	private readonly Dictionary<Guid, Action> _respawnersById = new();
	private readonly ISimulationEngine _simulationEngine = Factory.CreateBarnesHut();
	private readonly Stopwatch _stopwatch = new();
	private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
	private bool _autoCenterViewport;
	private bool _closedBoundaries = true;
	private bool _elasticCollisions = true;
	private bool _isEntityPresetSelectionVisible;
	private bool _isHelpVisible;
	private bool _isRunning = true;
	private int _isSimulating;
	private Entity? _selectedEntity;
	private EntityPreset _selectedEntityPreset;
	private bool _showPath = true;
	private double _timeScale = 1;

	// Track process CPU for accurate utilization and apply EMA smoothing
	private double _cpuUtilizationEma; // default 0.0
	private const double CpuUtilizationAlpha = 0.2; // smoothing factor

    #endregion

    #region Construction

    [SuppressMessage("Usage", "VSTHRD101:Avoid unsupported async delegates", Justification = "<Pending>")]
    public World()
	{
		_selectedEntityPreset = EntityPresets[0];

		_respawnersById[_randomRespawnerId] = () => CreateRandomEntities(1, true);
		_respawnersById[_randomOrbittingRespawnerId] = () => CreateRandomOrbitEntities(1, true);

		Entities.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(EntityCount));
		Viewport.PropertyChanged += (_, _) => Updated?.Invoke(this, EventArgs.Empty);

		//var d = new Win32.DEVMODE();

		//Win32.EnumDisplaySettings(null, 0, ref d);

		DisplayFrequency = 60;//d.dmDisplayFrequency;
		_stopwatch.Start();

		_timer.Tick += async (_, _) => await SimulateAsync();
		_timer.Interval = TimeSpan.FromSeconds(1.0d / DisplayFrequency);
		_timer.Start();
	}

	#endregion

	#region Interface

	public static int GetPreferredChunkSize<T>(IReadOnlyCollection<T> collection)
		=> collection.Count / Environment.ProcessorCount;

	public event EventHandler? Updated;

	public int DisplayFrequency { get; }

	public double TimeScale { get => _timeScale; set => SetProperty(ref _timeScale, value); }

	public double TimeScaleFactor
		=> Math.Pow(10, TimeScale);

	public EntityPreset[] EntityPresets { get; } =
		{
			EntityPreset.FromDensity("Eisenkugel klein", 7874, 10, Color.DarkGray, Color.White, 2.0d, new("C53FA0C5-AB12-43F7-9548-C098D5C44ADF")),
			EntityPreset.FromDensity("Eisenkugel mittel", 7874, 20, Color.DarkGray, Color.White, 2.0d,
									 new("CB30E40F-FB49-4688-94D1-3F1FB5C3F813")),
			EntityPreset.FromDensity("Eisenkugel groß", 7874, 100, Color.DarkGray, Color.White, 2.0d, new("03F7274E-B6C5-46E8-B8F8-03C969C79B49")),
			new("Mittelschwer+Klein", 100000000000, 20, Color.Green, new("98E60B8E-4461-4895-9107-A1FF5C9B9D64")),
			new("Leicht+Groß", 1000000000, 100, Color.Red, new("B6BBB8AC-109C-4CA1-96E1-976EABED256E")),
			new("Schwer+Groß", 1000000000000, 100, Color.Yellow, new("4F2D1D6B-0ED2-405E-8617-1B5073425F95")),
			new("Schwer+Klein", 1000000000000, 10, Color.Black, Color.White, 2.0d, new("0514F35B-029F-4E91-8071-81FD31C570E0")),
			new("Leicht+Klein", 1000, 20, Color.Blue, new("90424708-FFF6-4BD1-ADAF-6A534BBBACAA")),
			//new EntityPreset("Mini schwarzes Loch", 13466353096409057727806678973.0d, 20, Brushes.Black, Brushes.White, 2.0d),

			new("Sonne", 1.9884E30d, 696342000.0d, Color.Yellow, new("30584A17-00EE-4B85-ACEB-EFCAF2606468")),
			new("Erde", 5.9724E24d, 12756270.0d / 2, Color.Blue, new("3E9965AB-3A11-414A-A455-50527F254036")),
			new("Mond", 7.346E22d, 3474000.0d / 2, Color.DarkGray, new("71A1DD4C-5B87-405C-8033-B033B46A5237"))
		};

	public ObservableCollection<Entity> Entities { get; } = new();

	public EntityPreset SelectedEntityPreset { get => _selectedEntityPreset; set => SetProperty(ref _selectedEntityPreset, value); }

	public bool ElasticCollisions { get => _elasticCollisions; set => SetProperty(ref _elasticCollisions, value); }

	public bool IsEntityPresetSelectionVisible { get => _isEntityPresetSelectionVisible; set => SetProperty(ref _isEntityPresetSelectionVisible, value); }

	public bool ClosedBoundaries { get => _closedBoundaries; set => SetProperty(ref _closedBoundaries, value); }

	public bool ShowPath { get => _showPath; set => SetProperty(ref _showPath, value); }

	public Viewport Viewport { get; } = new();

	IViewport IWorld.Viewport
		=> Viewport;

	public TimeSpan RuntimeInSeconds { get; private set; }

	public int CpuUtilizationInPercent { get; private set; }

	public int EntityCount
		=> Entities.Count;

	public bool AutoCenterViewport { get => _autoCenterViewport; set => SetProperty(ref _autoCenterViewport, value); }

	public Entity? SelectedEntity { get => _selectedEntity; set => SetProperty(ref _selectedEntity, value); }

	public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

	public Guid? CurrentRespawnerId { get; set; }

	public bool IsHelpVisible { get => _isHelpVisible; set => SetProperty(ref _isHelpVisible, value); }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
    public void CreateRandomEntities(int count, bool enableRespawn)
	{
		var rnd = new Random();

		var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

		for(var i = 0; i < count; i++)
		{
			var position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

			while(Entities.Any(e => (e.Position - position).Length <= e.r + SelectedEntityPreset.r))
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

			while(Entities.Any(e => (e.Position - position).Length <= e.r + SelectedEntityPreset.r))
				position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

			CreateOrbitEntity(position, Vector2D.Zero);

			CurrentRespawnerId = enableRespawn
									 ? _randomOrbittingRespawnerId
									 : null;
		}
	}

	public void CreateEntity(Vector2D position, Vector2D velocity)
		=> Entities.Add(new(position,
							SelectedEntityPreset.r,
							SelectedEntityPreset.m,
							velocity,
							Vector2D.Zero,
							this, SelectedEntityPreset.Fill, SelectedEntityPreset.Stroke, SelectedEntityPreset.StrokeWidth));

	public void CreateOrbitEntity(Vector2D position, Vector2D velocity)
	{
		var nearestEntity = SelectedEntity
							?? Entities.OrderByDescending(p => G * p.m / ((p.Position - position).Length * (p.Position - position).Length))
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
		var g = G * (SelectedEntityPreset.m * nearestEntity.m) / dist.LengthSquared * -dist.Unit();
		var v = (1 + velocity.Length) * direction * Math.Sqrt(g.Length / SelectedEntityPreset.m * dist.Length) * dist.Norm().Unit() +
				nearestEntity.v;

		CreateEntity(position, v);
	}

	public void SelectEntity(Point viewportPoint, double viewportSearchRadius)
	{
		var pos = Viewport.ToWorld(viewportPoint);

		SelectedEntity = Entities.Where(e => (e.Position - pos).Length <= e.r + viewportSearchRadius / Viewport.ScaleFactor)
								 .OrderBy(e => (e.Position - pos).Length - (e.r + viewportSearchRadius / Viewport.ScaleFactor))
								 .FirstOrDefault();
	}

	public void AutoScaleAndCenterViewport()
	{
		if(!Entities.Any())
			return;

		var previousSize = Viewport.Size;
		var topLeft = new Vector2D(Entities.Min(e => e.Position.X - e.r),
								   Entities.Min(e => e.Position.Y - e.r));
		var bottomRight = new Vector2D(Entities.Max(e => e.Position.X + e.r),
									   Entities.Max(e => e.Position.Y + e.r));
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
		Entities.Clear();
		RuntimeInSeconds = TimeSpan.Zero;
		SelectedEntity = null;

		var viewportSize = Viewport.Size;

		Viewport.TopLeft = -viewportSize / 2;
		Viewport.BottomRight = viewportSize / 2;
	}

	public async Task SaveAsync(string filePath)
	{
#pragma warning disable CS8601 // Possible null reference assignment.
        var state = new State
					{
						Viewport = new()
								   {
									   TopLeft = Viewport.TopLeft,
									   BottomRight = Viewport.BottomRight,
									   Scale = Viewport.Scale
								   },
						AutoCenterViewport = AutoCenterViewport,
						ClosedBoundaries = ClosedBoundaries,
						ElasticCollisions = ElasticCollisions,
						ShowPath = ShowPath,
						TimeScale = TimeScale,
						SelectedEntityPresetId = SelectedEntityPreset.Id,
						RespawnerId = CurrentRespawnerId,
						Entities = Entities.Select(e => new State.EntityState
														{
															m = e.m,
															Position = e.Position,
															v = e.v,
															r = e.r,
															StrokeWidth = e.StrokeWidth,
															FillColor = e.Fill
																		 .ToString(),
															StrokeColor = e.Stroke
																		   .ToString()
														})
										   .ToArray()
					};
#pragma warning restore CS8601 // Possible null reference assignment.

        await using var swr = File.CreateText(filePath);
		await JsonSerializer.SerializeAsync(swr.BaseStream, state);
	}

	public async Task OpenAsync(string filePath)
	{
		using var srd = File.OpenText(filePath);

		var state = await JsonSerializer.DeserializeAsync<State>(srd.BaseStream);

		Reset();

		if(null == state)
			return;

		Viewport.TopLeft = state.Viewport.TopLeft;
		Viewport.BottomRight = state.Viewport.BottomRight;
		Viewport.Scale = state.Viewport.Scale;
		AutoCenterViewport = state.AutoCenterViewport;
		ClosedBoundaries = state.ClosedBoundaries;
		ElasticCollisions = state.ElasticCollisions;
		SelectedEntityPreset = EntityPresets.First(p => p.Id == state.SelectedEntityPresetId);
		CurrentRespawnerId = state.RespawnerId;
		ShowPath = state.ShowPath;
		TimeScale = state.TimeScale;

		if(null == state.Entities)
			return;

		foreach(var entity in state.Entities)
			Entities.Add(new(entity.Position,
							 entity.r,
							 entity.m,
							 entity.v,
							 Vector2D.Zero,
							 this,
							 Color.Parse(entity.FillColor),
							 Color.Parse(entity.StrokeColor),
							 entity.StrokeWidth));
	}

	#endregion

	#region Implementation

	private async Task SimulateAsync()
	{
		if(1 == Interlocked.CompareExchange(ref _isSimulating, 1, 0))
			return;

		var start = _stopwatch.Elapsed;
		var startProcessCpu = Process.GetCurrentProcess().TotalProcessorTime;
		var deltaTime = TimeSpan.FromSeconds(1.0d / DisplayFrequency * TimeScaleFactor);

		if(IsRunning)
		{
			await UpdateAllEntitiesAsync(deltaTime);

			if(AutoCenterViewport)
				DoAutoCenterViewport();

			RuntimeInSeconds += deltaTime;
		}

		var end = _stopwatch.Elapsed;
		var endProcessCpu = Process.GetCurrentProcess().TotalProcessorTime;
		var wallElapsed = end - start;
		var cpuElapsed = endProcessCpu - startProcessCpu;
		var coreCount = Environment.ProcessorCount;
		var instantCpuPercent = wallElapsed.TotalMilliseconds > 0
			? Math.Min(100.0, Math.Max(0.0, cpuElapsed.TotalMilliseconds / wallElapsed.TotalMilliseconds * (100.0 / coreCount)))
			: 0.0;

		// Exponential moving average for stability
		_cpuUtilizationEma = CpuUtilizationAlpha * instantCpuPercent + (1.0 - CpuUtilizationAlpha) * _cpuUtilizationEma;
		CpuUtilizationInPercent = (int)Math.Round(_cpuUtilizationEma);

		Updated?.Invoke(this, EventArgs.Empty);

		_isSimulating = 0;
	}

	private async Task UpdateAllEntitiesAsync(TimeSpan deltaTime)
	{
		if(!Entities.Any())
			return;

		var entities = Entities.ToArray();

		await _simulationEngine.SimulateAsync(entities, deltaTime);

		var respawner = CurrentRespawnerId.HasValue
							? _respawnersById[CurrentRespawnerId.Value]
							: () => {};

		// Absorbierte Objekte entfernen
		foreach(var absorbedEntities in entities.Where(e => e.IsAbsorbed).ToArray())
		{
			Entities.Remove(absorbedEntities);

			if(!CurrentRespawnerId.HasValue)
				continue;

			respawner();
		}
	}

	private void DoAutoCenterViewport()
	{
		if(!Entities.Any())
			return;

		var previousSize = Viewport.Size;
		var topLeft = new Vector2D(Entities.Min(e => e.Position.X - e.r),
								   Entities.Min(e => e.Position.Y - e.r));
		var bottomRight = new Vector2D(Entities.Max(e => e.Position.X + e.r),
									   Entities.Max(e => e.Position.Y + e.r));
		var center = topLeft + (bottomRight - topLeft) / 2;

		Viewport.TopLeft = center - previousSize / 2;
		Viewport.BottomRight = center + previousSize / 2;
	}

	#endregion
}