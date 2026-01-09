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

			// Manual Any
			while (Entities.Count > 0)
			{
				bool overlap = false;
				for (int j = 0; j < Entities.Count; j++)
				{
					var e = Entities[j];
					var d = e.Position - position;
					if (Math.Sqrt(d.LengthSquared) <= e.r + SelectedEntityPreset.r)
					{
						overlap = true;
						break;
					}
				}
				if (!overlap) break;
				position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;
			}

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

			while (Entities.Count > 0)
			{
				bool overlap = false;
				for (int j = 0; j < Entities.Count; j++)
				{
					var e = Entities[j];
					var d = e.Position - position;
					if (Math.Sqrt(d.LengthSquared) <= e.r + SelectedEntityPreset.r)
					{
						overlap = true;
						break;
					}
				}
				if (!overlap) break;
				position = new Vector2D(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;
			}

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
		// Find nearest entity without LINQ
		Entity? nearestEntity = SelectedEntity;
		if (nearestEntity is null && Entities.Count > 0)
		{
			double bestScore = double.NegativeInfinity;
			for (int i = 0; i < Entities.Count; i++)
			{
				var p = Entities[i];
				var d = p.Position - position;
				var score = G * p.m / (d.LengthSquared);
				if (score > bestScore)
				{
					bestScore = score;
					nearestEntity = p;
				}
			}
		}

		if (nearestEntity is null)
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
		// Manual search for closest entity meeting radius criteria
		Entity? best = null;
		double bestMetric = double.PositiveInfinity;
		double threshold = viewportSearchRadius / Viewport.ScaleFactor;
		for (int i = 0; i < Entities.Count; i++)
		{
			var e = Entities[i];
			var d = e.Position - pos;
			var len = Math.Sqrt(d.LengthSquared);
			if (len <= e.r + threshold)
			{
				var metric = len - (e.r + threshold);
				if (metric < bestMetric)
				{
					bestMetric = metric;
					best = e;
				}
			}
		}
		SelectedEntity = best;
	}

	public void AutoScaleAndCenterViewport()
	{
		if (Entities.Count == 0)
			return;

		var previousSize = Viewport.Size;
		double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
		double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
		for (int i = 0; i < Entities.Count; i++)
		{
			var e = Entities[i];
			var px = e.Position.X; var py = e.Position.Y; var r = e.r;
			var left = px - r; var right = px + r; var top = py - r; var bottom = py + r;
			if (left < minX) minX = left;
			if (top < minY) minY = top;
			if (right > maxX) maxX = right;
			if (bottom > maxY) maxY = bottom;
		}
		var topLeft = new Vector2D(minX, minY);
		var bottomRight = new Vector2D(maxX, maxY);
		var center = topLeft + (bottomRight - topLeft) / 2;
		var newSize = bottomRight - topLeft;

		if (newSize.X / newSize.Y < previousSize.X / previousSize.Y)
		{
			newSize = new Vector2D(newSize.Y * previousSize.X / previousSize.Y, newSize.Y);
		}
		if (newSize.X / newSize.Y > previousSize.X / previousSize.Y)
		{
			newSize = new Vector2D(newSize.X, newSize.X * previousSize.Y / previousSize.X);
		}

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
        var list = new List<State.EntityState>(Entities.Count);
        for (int i = 0; i < Entities.Count; i++)
        {
            var e = Entities[i];
            list.Add(new State.EntityState
            {
                m = e.m,
                Position = e.Position,
                v = e.v,
                r = e.r,
                StrokeWidth = e.StrokeWidth,
                FillColor = e.Fill.ToString(),
                StrokeColor = e.Stroke.ToString()
            });
        }
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
						Entities = list.ToArray()
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
        if (state is null)
            return;
        Viewport.TopLeft = state.Viewport.TopLeft;
        Viewport.BottomRight = state.Viewport.BottomRight;
        Viewport.Scale = state.Viewport.Scale;
        AutoCenterViewport = state.AutoCenterViewport;
        ClosedBoundaries = state.ClosedBoundaries;
        ElasticCollisions = state.ElasticCollisions;
        SelectedEntityPreset = Array.Find(EntityPresets, p => p.Id == state.SelectedEntityPresetId) ?? _selectedEntityPreset;
        CurrentRespawnerId = state.RespawnerId;
        ShowPath = state.ShowPath;
        TimeScale = state.TimeScale;
        if (state.Entities is null)
            return;
        foreach (var entity in state.Entities)
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
		var deltaTime = TimeSpan.FromSeconds(1.0d / DisplayFrequency * TimeScaleFactor);

		if(IsRunning)
		{
			await UpdateAllEntitiesAsync(deltaTime);

			if(AutoCenterViewport)
				DoAutoCenterViewport();

			RuntimeInSeconds += deltaTime;
		}

		CpuUtilizationInPercent = (int)Math.Round((_stopwatch.Elapsed - start).TotalSeconds * DisplayFrequency * 100.0d);

		Updated?.Invoke(this, EventArgs.Empty);

		_isSimulating = 0;
	}

	private async Task UpdateAllEntitiesAsync(TimeSpan deltaTime)
	{
		if (Entities.Count == 0)
			return;

		var entities = new Entity[Entities.Count];
		Entities.CopyTo(entities, 0);
		await _simulationEngine.SimulateAsync(entities, deltaTime);

		var respawner = CurrentRespawnerId.HasValue ? _respawnersById[CurrentRespawnerId.Value] : () => { };

		// Remove absorbed without LINQ
		for (int i = 0; i < entities.Length; i++)
		{
			var e = entities[i];
			if (e.IsAbsorbed)
			{
				Entities.Remove(e);
				if (CurrentRespawnerId.HasValue)
					respawner();
			}
		}
	}

	private void DoAutoCenterViewport()
	{
		if (Entities.Count == 0)
			return;

		var previousSize = Viewport.Size;
		double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
		double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
		for (int i = 0; i < Entities.Count; i++)
		{
			var e = Entities[i];
			var px = e.Position.X; var py = e.Position.Y; var r = e.r;
			var left = px - r; var right = px + r; var top = py - r; var bottom = py + r;
			if (left < minX) minX = left;
			if (top < minY) minY = top;
			if (right > maxX) maxX = right;
			if (bottom > maxY) maxY = bottom;
		}
		var topLeft = new Vector2D(minX, minY);
		var bottomRight = new Vector2D(maxX, maxY);
		var center = topLeft + (bottomRight - topLeft) / 2;

		Viewport.TopLeft = center - previousSize / 2;
		Viewport.BottomRight = center + previousSize / 2;
	}

	#endregion
}