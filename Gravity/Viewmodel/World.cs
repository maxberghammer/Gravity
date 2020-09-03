using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Gravity.Viewmodel
{
	internal class World : NotifyPropertyChanged
	{
		#region Fields

		public static readonly double G = Math.Pow(6.67430d, -11.0);
		private readonly int mDisplayFrequency;
		private EntityPreset mSelectedEntityPreset;
		private bool mElasticCollisions = true;
		private bool mClosedBoundaries = true;
		private TimeSpan mRuntimeInSeconds;
		private bool mAutoCenterViewport;
		private Entity mSelectedEntity;
		private bool mShowPath = true;
		private TimeSpan? mLastUpdateTime;
		private bool mIsRunning = true;
		private int mCpuUtilizationInPercent;
		private double mTimeScale = 1;
		private bool mIsEntityPresetSelectionVisible;
		private bool mIsHelpVisible;

		#endregion

		#region Construction

		public World()
		{
			SelectedEntityPreset = EntityPresets.First();

			Entities.CollectionChanged += (sender, args) => RaisePropertyChanged(nameof(EntityCount));
			Viewport.PropertyChanged += (sender, args) => Updated?.Invoke(this, EventArgs.Empty);

			var d = new Win32.DEVMODE();

			Win32.EnumDisplaySettings(null, 0, ref d);

			mDisplayFrequency = d.dmDisplayFrequency;
			mStopwatch.Start();

			Application.Current.Dispatcher.InvokeAsync(async () => await SimulateAsync(), DispatcherPriority.Background);
		}

		#endregion

		#region Interface

		public event EventHandler Updated;

		public double TimeScale { get => mTimeScale; set => SetProperty(ref mTimeScale, value); }

		public double TimeScaleFactor
			=> Math.Pow(10, TimeScale);

		public EntityPreset[] EntityPresets { get; } =
			{
				EntityPreset.FromDensity("Eisenkugel klein", 7874, 10, Brushes.DarkGray, Brushes.White, 2.0d),
				EntityPreset.FromDensity("Eisenkugel mittel", 7874, 20, Brushes.DarkGray, Brushes.White, 2.0d),
				EntityPreset.FromDensity("Eisenkugel groß", 7874, 100, Brushes.DarkGray, Brushes.White, 2.0d),

				new EntityPreset("Mittelschwer+Klein", 100000000000, 20, Brushes.Green),
				new EntityPreset("Leicht+Groß", 1000000000, 100, Brushes.Red),
				new EntityPreset("Schwer+Groß", 1000000000000, 100, Brushes.Yellow),
				new EntityPreset("Schwer+Klein", 1000000000000, 10, Brushes.Black, Brushes.White, 2.0d),
				new EntityPreset("Leicht+Klein", 1000, 20, Brushes.Blue),
				//new EntityPreset("Mini schwarzes Loch", 13466353096409057727806678973.0d, 20, Brushes.Black, Brushes.White, 2.0d),

				new EntityPreset("Sonne", 1.9884E30d, 696342000.0d, Brushes.Yellow),
				new EntityPreset("Erde", 5.9724E24d, 12756270.0d / 2, Brushes.Blue),
				new EntityPreset("Mond", 7.346E22d, 3474000.0d / 2, Brushes.DarkGray)
			};

		public ObservableCollection<Entity> Entities { get; } = new ObservableCollection<Entity>();

		public EntityPreset SelectedEntityPreset { get => mSelectedEntityPreset; set => SetProperty(ref mSelectedEntityPreset, value); }

		public bool ElasticCollisions { get => mElasticCollisions; set => SetProperty(ref mElasticCollisions, value); }

		public bool IsEntityPresetSelectionVisible { get => mIsEntityPresetSelectionVisible; set => SetProperty(ref mIsEntityPresetSelectionVisible, value); }

		public bool ClosedBoundaries { get => mClosedBoundaries; set => SetProperty(ref mClosedBoundaries, value); }

		public bool ShowPath { get => mShowPath; set => SetProperty(ref mShowPath, value); }

		public Viewport Viewport { get; } = new Viewport();

		public TimeSpan RuntimeInSeconds { get => mRuntimeInSeconds; set => SetProperty(ref mRuntimeInSeconds, value); }

		public int CpuUtilizationInPercent { get => mCpuUtilizationInPercent; set => SetProperty(ref mCpuUtilizationInPercent, value); }

		public int EntityCount
			=> Entities.Count;

		public bool AutoCenterViewport { get => mAutoCenterViewport; set => SetProperty(ref mAutoCenterViewport, value); }

		public Entity SelectedEntity { get => mSelectedEntity; set => SetProperty(ref mSelectedEntity, value); }

		public bool IsRunning { get => mIsRunning; set => SetProperty(ref mIsRunning, value); }

		public Action RebuildAbsorbed { get; set; }

		public bool IsHelpVisible { get => mIsHelpVisible; set => SetProperty(ref mIsHelpVisible, value); }

		public void CreateRandomEntities(int aCount, bool aRebuildAbsorbed)
		{
			var rnd = new Random();

			var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

			for (var i = 0; i < aCount; i++)
			{
				var position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				while (Entities.Any(e => (e.Position - position).Length <= (e.r + SelectedEntityPreset.r)))
					position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				CreateEntity(position, VectorExtensions.Zero);

				RebuildAbsorbed = aRebuildAbsorbed
									  ? (Action)(() => CreateRandomEntities(1, true))
									  : null;
			}
		}

		public void CreateRandomOrbitEntities(int aCount, bool aRebuildAbsorbed)
		{
			var rnd = new Random();

			var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

			for (var i = 0; i < aCount; i++)
			{
				var position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				while (Entities.Any(e => (e.Position - position).Length <= (e.r + SelectedEntityPreset.r)))
					position = new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + Viewport.TopLeft;

				CreateOrbitEntity(position, VectorExtensions.Zero);

				RebuildAbsorbed = aRebuildAbsorbed
									  ? (Action)(() => CreateRandomOrbitEntities(1, true))
									  : null;
			}
		}

		public void CreateEntity(Vector aPosition, Vector aVelocity)
			=> Entities.Add(new Entity(aPosition,
									   SelectedEntityPreset.r,
									   SelectedEntityPreset.m,
									   aVelocity,
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

			SelectedEntity = Entities.FirstOrDefault(e => (e.Position - pos).Length <= (e.r + aViewportSearchRadius / Viewport.ScaleFactor));
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

			var viewportSize = Viewport.Size;

			Viewport.TopLeft = -viewportSize / 2;
			Viewport.BottomRight = viewportSize / 2;
		}

		#endregion

		private readonly Stopwatch mStopwatch= new Stopwatch();

		#region Implementation

		private async Task SimulateAsync()
		{
			var start = mStopwatch.Elapsed;
			var deltaTime = mLastUpdateTime.HasValue 
				? TimeSpan.FromSeconds((start - mLastUpdateTime.Value).TotalSeconds * TimeScaleFactor) 
				: TimeSpan.Zero;

			mLastUpdateTime = start;

			if (IsRunning)
			{
				await UpdateAllEntitiesAsync(deltaTime);

				if (AutoCenterViewport)
					DoAutoCenterViewport();

				RuntimeInSeconds += deltaTime;
			}

			CpuUtilizationInPercent = (int)Math.Round((mStopwatch.Elapsed - start).TotalSeconds * mDisplayFrequency * 100.0d);

			Updated?.Invoke(this, EventArgs.Empty);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
			Application.Current.Dispatcher.InvokeAsync(async () => await SimulateAsync(), DispatcherPriority.Background);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
		}

		private async Task UpdateAllEntitiesAsync(TimeSpan aDeltaTime)
		{
			var entities = Entities.ToArray();

			// Positionen updaten
			foreach (var entity in entities)
				entity.UpdatePosition(aDeltaTime);

			// Physik anwenden
			await ApplyPhysicsAsync(entities.Where(e => !e.IsAbsorbed)
											.ToArray());

			// Absorbierte Objekte entfernen
			foreach (var entityToDelete in entities.Where(e => e.IsAbsorbed).ToArray())
			{
				Entities.Remove(entityToDelete);

				RebuildAbsorbed?.Invoke();
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

		private static async Task ApplyPhysicsAsync(IReadOnlyCollection<Entity> aEntities)
			=> await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										   .Select(chunk => Task.Run(() =>
																	 {
																		 foreach (var entity in chunk)
																			 entity.ApplyPhysics(aEntities.Except(entity));
																	 })));

		#endregion
	}
}