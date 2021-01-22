using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Gravity.SimulationEngine;

namespace Gravity.Viewmodel
{
	internal class World : NotifyPropertyChanged
	{
		#region Internal types

		private class State
		{
			#region Internal types

			public class ViewportState
			{
				#region Interface

				public Vector TopLeft { get; init; }

				public Vector BottomRight { get; init; }

				public double Scale { get; init; }

				#endregion
			}

			public class EntityState
			{
				#region Interface

				public string FillColor { get; init; }

				public string StrokeColor { get; init; }

				public double StrokeWidth { get; init; }

				public Vector Position { get; init; }

				// ReSharper disable once InconsistentNaming
				[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
				public Vector v { get; init; }

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

			public ViewportState Viewport { get; init; }

			public double TimeScale { get; init; }

			public bool ElasticCollisions { get; init; }

			public bool ClosedBoundaries { get; init; }

			public bool ShowPath { get; init; }

			public bool AutoCenterViewport { get; init; }

			public Guid SelectedEntityPresetId { get; init; }

			public Guid? RespawnerId { get; init; }

			public EntityState[] Entities { get; init; }

			#endregion
		}

		#endregion

		#region Fields

		public static readonly double G = Math.Pow(6.67430d, -11.0);
		private static readonly Guid mRandomRespawnerId = new("7E4948F8-CFA5-45A3-BB05-48CB4AAB13B1");
		private static readonly Guid mRandomOrbittingRespawnerId = new("F02C36A4-FEC2-49AD-B3DA-C7E9B6E4C361");

		private readonly Stopwatch mStopwatch = new();
		private readonly DispatcherTimer mTimer = new(DispatcherPriority.Render);
		private readonly Dictionary<Guid, Action> mRespawnersById = new();
		private readonly ISimulationEngine mSimulationEngine = new BarnesHutSimulationEngine();

		private EntityPreset mSelectedEntityPreset;
		private bool mElasticCollisions = true;
		private bool mClosedBoundaries = true;
		private bool mAutoCenterViewport;
		private Entity mSelectedEntity;
		private bool mShowPath = true;
		private bool mIsRunning = true;
		private double mTimeScale = 1;
		private bool mIsEntityPresetSelectionVisible;
		private bool mIsHelpVisible;
		private int mIsSimulating;

		#endregion

		#region Construction

		public World()
		{
			SelectedEntityPreset = EntityPresets.First();

			mRespawnersById[mRandomRespawnerId] = () => CreateRandomEntities(1, true);
			mRespawnersById[mRandomOrbittingRespawnerId] = () => CreateRandomOrbitEntities(1, true);

			Entities.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(EntityCount));
			Viewport.PropertyChanged += (_, _) => Updated?.Invoke(this, EventArgs.Empty);

			var d = new Win32.DEVMODE();

			Win32.EnumDisplaySettings(null, 0, ref d);

			DisplayFrequency = d.dmDisplayFrequency;
			mStopwatch.Start();

			mTimer.Tick += async (_, _) => await SimulateAsync();
			mTimer.Interval = TimeSpan.FromSeconds(1.0d / DisplayFrequency);
			mTimer.Start();
		}

		#endregion

		#region Interface

		public event EventHandler Updated;

		public int DisplayFrequency { get; }

		public double TimeScale { get => mTimeScale; set => SetProperty(ref mTimeScale, value); }

		public double TimeScaleFactor
			=> Math.Pow(10, TimeScale);

		public EntityPreset[] EntityPresets { get; } =
			{
				EntityPreset.FromDensity("Eisenkugel klein", 7874, 10, Brushes.DarkGray, Brushes.White, 2.0d, new Guid("C53FA0C5-AB12-43F7-9548-C098D5C44ADF")),
				EntityPreset.FromDensity("Eisenkugel mittel", 7874, 20, Brushes.DarkGray, Brushes.White, 2.0d,
										 new Guid("CB30E40F-FB49-4688-94D1-3F1FB5C3F813")),
				EntityPreset.FromDensity("Eisenkugel groß", 7874, 100, Brushes.DarkGray, Brushes.White, 2.0d, new Guid("03F7274E-B6C5-46E8-B8F8-03C969C79B49")),

				new("Mittelschwer+Klein", 100000000000, 20, Brushes.Green, new Guid("98E60B8E-4461-4895-9107-A1FF5C9B9D64")),
				new("Leicht+Groß", 1000000000, 100, Brushes.Red, new Guid("B6BBB8AC-109C-4CA1-96E1-976EABED256E")),
				new("Schwer+Groß", 1000000000000, 100, Brushes.Yellow, new Guid("4F2D1D6B-0ED2-405E-8617-1B5073425F95")),
				new("Schwer+Klein", 1000000000000, 10, Brushes.Black, Brushes.White, 2.0d, new Guid("0514F35B-029F-4E91-8071-81FD31C570E0")),
				new("Leicht+Klein", 1000, 20, Brushes.Blue, new Guid("90424708-FFF6-4BD1-ADAF-6A534BBBACAA")),
				//new EntityPreset("Mini schwarzes Loch", 13466353096409057727806678973.0d, 20, Brushes.Black, Brushes.White, 2.0d),

				new("Sonne", 1.9884E30d, 696342000.0d, Brushes.Yellow, new Guid("30584A17-00EE-4B85-ACEB-EFCAF2606468")),
				new("Erde", 5.9724E24d, 12756270.0d / 2, Brushes.Blue, new Guid("3E9965AB-3A11-414A-A455-50527F254036")),
				new("Mond", 7.346E22d, 3474000.0d / 2, Brushes.DarkGray, new Guid("71A1DD4C-5B87-405C-8033-B033B46A5237"))
			};

		public ObservableCollection<Entity> Entities { get; } = new();

		public EntityPreset SelectedEntityPreset { get => mSelectedEntityPreset; set => SetProperty(ref mSelectedEntityPreset, value); }

		public bool ElasticCollisions { get => mElasticCollisions; set => SetProperty(ref mElasticCollisions, value); }

		public bool IsEntityPresetSelectionVisible { get => mIsEntityPresetSelectionVisible; set => SetProperty(ref mIsEntityPresetSelectionVisible, value); }

		public bool ClosedBoundaries { get => mClosedBoundaries; set => SetProperty(ref mClosedBoundaries, value); }

		public bool ShowPath { get => mShowPath; set => SetProperty(ref mShowPath, value); }

		public Viewport Viewport { get; } = new();

		public TimeSpan RuntimeInSeconds { get; private set; }

		public int CpuUtilizationInPercent { get; private set; }

		public int EntityCount
			=> Entities.Count;

		public bool AutoCenterViewport { get => mAutoCenterViewport; set => SetProperty(ref mAutoCenterViewport, value); }

		public Entity SelectedEntity { get => mSelectedEntity; set => SetProperty(ref mSelectedEntity, value); }

		public bool IsRunning { get => mIsRunning; set => SetProperty(ref mIsRunning, value); }

		public Guid? CurrentRespawnerId { get; set; }

		public bool IsHelpVisible { get => mIsHelpVisible; set => SetProperty(ref mIsHelpVisible, value); }

		public void CreateRandomEntities(int aCount, bool aEnableRespawn)
		{
			var rnd = new Random();

			var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

			for (var i = 0; i < aCount; i++)
			{
				var position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				while (Entities.Any(e => (e.Position - position).Length <= (e.r + SelectedEntityPreset.r)))
					position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				CreateEntity(position, VectorExtensions.Zero);

				CurrentRespawnerId = aEnableRespawn
										 ? mRandomRespawnerId
										 : (Guid?)null;
			}
		}

		public void CreateRandomOrbitEntities(int aCount, bool aEnableRespawn)
		{
			var rnd = new Random();

			var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

			for (var i = 0; i < aCount; i++)
			{
				var position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				while (Entities.Any(e => (e.Position - position).Length <= (e.r + SelectedEntityPreset.r)))
					position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				CreateOrbitEntity(position, VectorExtensions.Zero);

				CurrentRespawnerId = aEnableRespawn
										 ? mRandomOrbittingRespawnerId
										 : (Guid?)null;
			}
		}

		public void CreateEntity(Vector aPosition, Vector aVelocity)
			=> Entities.Add(new Entity(aPosition,
									   SelectedEntityPreset.r,
									   SelectedEntityPreset.m,
									   aVelocity,
									   VectorExtensions.Zero,
									   this, SelectedEntityPreset.Fill, SelectedEntityPreset.Stroke, SelectedEntityPreset.StrokeWidth));

		public void CreateOrbitEntity(Vector aPosition, Vector aVelocity)
		{
			var nearestEntity = SelectedEntity
								?? Entities.OrderByDescending(p => G * p.m / ((p.Position - aPosition).Length * (p.Position - aPosition).Length))
										   .FirstOrDefault();

			if (null == nearestEntity)
			{
				CreateEntity(aPosition, new Vector(0, 0));
				return;
			}

			var dist = aPosition - nearestEntity.Position;
			var direction = ((dist.Norm().Unit() - aVelocity.Unit()).Length > (-dist.Norm().Unit() - aVelocity.Unit()).Length)
								? -1
								: 1;
			var g = G * (SelectedEntityPreset.m * nearestEntity.m) / dist.LengthSquared * -dist.Unit();
			var v = (1 + aVelocity.Length) * direction * Math.Sqrt(g.Length / SelectedEntityPreset.m * dist.Length) * dist.Norm().Unit() +
					nearestEntity.v;

			CreateEntity(aPosition, v);
		}

		public void SelectEntity(Point aViewportPoint, double aViewportSearchRadius)
		{
			var pos = Viewport.ToWorld(aViewportPoint);

			SelectedEntity = Entities.Where(e => (e.Position - pos).Length <= (e.r + aViewportSearchRadius / Viewport.ScaleFactor))
									 .OrderBy(e => (e.Position - pos).Length - (e.r + aViewportSearchRadius / Viewport.ScaleFactor))
									 .FirstOrDefault();
		}

		public void AutoScaleAndCenterViewport()
		{
			if (!Entities.Any())
				return;

			var previousSize = Viewport.Size;
			var topLeft = new Vector(Entities.Min(e => e.Position.X - e.r),
									 Entities.Min(e => e.Position.Y - e.r));
			var bottomRight = new Vector(Entities.Max(e => e.Position.X + e.r),
										 Entities.Max(e => e.Position.Y + e.r));
			var center = topLeft + (bottomRight - topLeft) / 2;
			var newSize = bottomRight - topLeft;

			if (newSize.X / newSize.Y < previousSize.X / previousSize.Y)
				newSize.X = newSize.Y * previousSize.X / previousSize.Y;
			if (newSize.X / newSize.Y > previousSize.X / previousSize.Y)
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

		public async Task SaveAsync(string aFilePath)
		{
			var state = new State
						{
							Viewport = new State.ViewportState
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
																			 ?.Color
																			 .ToString(),
																StrokeColor = e.Stroke
																			   ?.Color
																			   .ToString()
															})
											   .ToArray()
						};

			await using var swr = File.CreateText(aFilePath);
			await JsonSerializer.SerializeAsync(swr.BaseStream, state);
		}

		public async Task OpenAsync(string aFilePath)
		{
			using var srd = File.OpenText(aFilePath);

			var state = await JsonSerializer.DeserializeAsync<State>(srd.BaseStream);

			Reset();

			if (null == state)
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

			if (null == state.Entities)
				return;

			var brushesByColor = new Dictionary<string, SolidColorBrush>();

			SolidColorBrush CreateBrush(string aColor)
			{
				if (string.IsNullOrEmpty(aColor))
					return null;

				if (brushesByColor.TryGetValue(aColor, out var brush))
					return brush;

				brush = brushesByColor[aColor] = new((Color)ColorConverter.ConvertFromString(aColor));
				brush.Freeze();

				return brush;
			}

			foreach (var entity in state.Entities)
				Entities.Add(new Entity(entity.Position,
										entity.r,
										entity.m,
										entity.v,
										VectorExtensions.Zero,
										this,
										CreateBrush(entity.FillColor),
										CreateBrush(entity.StrokeColor),
										entity.StrokeWidth));
		}

		#endregion

		#region Implementation

		private async Task SimulateAsync()
		{
			if (1 == Interlocked.CompareExchange(ref mIsSimulating, 1, 0))
				return;

			var start = mStopwatch.Elapsed;
			var deltaTime = TimeSpan.FromSeconds(1.0d / DisplayFrequency * TimeScaleFactor);

			if (IsRunning)
			{
				await UpdateAllEntitiesAsync(deltaTime);

				if (AutoCenterViewport)
					DoAutoCenterViewport();

				RuntimeInSeconds += deltaTime;
			}

			CpuUtilizationInPercent = (int)Math.Round((mStopwatch.Elapsed - start).TotalSeconds * DisplayFrequency * 100.0d);

			Updated?.Invoke(this, EventArgs.Empty);

			mIsSimulating = 0;
		}

		private async Task UpdateAllEntitiesAsync(TimeSpan aDeltaTime)
		{
			if (!Entities.Any())
				return;

			var entities = Entities.ToArray();

			await mSimulationEngine.SimulateAsync(entities, aDeltaTime);

			var respawner = CurrentRespawnerId.HasValue
								? mRespawnersById[CurrentRespawnerId.Value]
								: () => { };

			// Absorbierte Objekte entfernen
			foreach (var absorbedEntities in entities.Where(e => e.IsAbsorbed).ToArray())
			{
				Entities.Remove(absorbedEntities);

				if (!CurrentRespawnerId.HasValue)
					continue;

				respawner();
			}
		}

		private void DoAutoCenterViewport()
		{
			if (!Entities.Any())
				return;

			var previousSize = Viewport.Size;
			var topLeft = new Vector(Entities.Min(e => e.Position.X - e.r),
									 Entities.Min(e => e.Position.Y - e.r));
			var bottomRight = new Vector(Entities.Max(e => e.Position.X + e.r),
										 Entities.Max(e => e.Position.Y + e.r));
			var center = topLeft + (bottomRight - topLeft) / 2;

			Viewport.TopLeft = center - previousSize / 2;
			Viewport.BottomRight = center + previousSize / 2;
		}

		#endregion
	}
}