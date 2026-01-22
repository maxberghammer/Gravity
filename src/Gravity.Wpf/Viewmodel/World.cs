// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
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
using Wellenlib;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class World : NotifyPropertyChanged,
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
	private readonly List<Body> _bodies = [];
	private readonly FrameDiagnostics _frameTiming = new();
	private readonly Dictionary<Guid, Action> _respawnersById = new();
	private readonly SimpleRng _rng = new();
	private readonly Stopwatch _simulationTime = new();
	private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
	private int _isSimulating;
	private TimeSpan _lastSimulationStep = TimeSpan.Zero;
	private ISimulationEngine? _simulationEngine;

	#endregion

	#region Construction

	[SuppressMessage("Usage", "VSTHRD101:Avoid unsupported async delegates", Justification = "<Pending>")]
	public World()
	{
		SelectedBodyPreset = BodyPresets[0];
		SelectedEngineType = EngineTypes[0];

		_respawnersById[_randomRespawnerId] = () => CreateRandomBodies(1, true, false);
		_respawnersById[_randomOrbittingRespawnerId] = () => CreateRandomBodies(1, true, true);

		//var d = new Win32.DEVMODE();

		//Win32.EnumDisplaySettings(null, 0, ref d);

		DisplayFrequency = 60; //d.dmDisplayFrequency;

		_simulationTime.Start();

		_timer.Tick += async (_, _) => await SimulateAsync();
		_timer.Interval = TimeSpan.FromSeconds(1.0d / DisplayFrequency);
		_timer.Start();
	}

	#endregion

	#region Interface

	public double TimeScale { get; set => SetProperty(ref field, value); }

	public int FramesPerSecond { get; set => SetProperty(ref field, value); }

	public BodyPreset SelectedBodyPreset { get; set => SetProperty(ref field, value); }

	public EngineType SelectedEngineType
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			ArgumentNullException.ThrowIfNull(value);
			_simulationEngine = Factory.Create(value.Type);
		}
	}

	public bool IsBodyPresetSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool IsEngineSelectionVisible { get; set => SetProperty(ref field, value); }

	public bool ShowPath { get; set => SetProperty(ref field, value); } = true;

	public bool AutoCenterViewport { get; set => SetProperty(ref field, value); }

	public Body? SelectedBody { get; set => SetProperty(ref field, value); }

	public bool IsHelpVisible { get; set => SetProperty(ref field, value); }

	public bool ElasticCollisions { get; set => SetProperty(ref field, value); } = true;

	public bool ClosedBoundaries { get; set => SetProperty(ref field, value); } = true;

	public bool IsRunning
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				_simulationTime.Start();
			else
				_simulationTime.Stop();
		}
	} = true;

	public int DisplayFrequency { get; }

	public double TimeScaleFactor
		=> Math.Pow(10, TimeScale);

	public IReadOnlyList<BodyPreset> BodyPresets { get; } =
		[
			BodyPreset.FromDensity("Eisenkugel klein", 7874, 10, Color.DarkGray, Color.White, 2.0d, new("C53FA0C5-AB12-43F7-9548-C098D5C44ADF")),
			BodyPreset.FromDensity("Eisenkugel mittel", 7874, 20, Color.DarkGray, Color.White, 2.0d,
								   new("CB30E40F-FB49-4688-94D1-3F1FB5C3F813")),
			BodyPreset.FromDensity("Eisenkugel groß", 7874, 100, Color.DarkGray, Color.White, 2.0d, new("03F7274E-B6C5-46E8-B8F8-03C969C79B49")),
			new("Mittelschwer+Klein", 100000000000, 20, Color.Green, new("98E60B8E-4461-4895-9107-A1FF5C9B9D64")),
			new("Leicht+Groß", 1000000000, 100, Color.Red, new("B6BBB8AC-109C-4CA1-96E1-976EABED256E")),
			new("Schwer+Groß", 1000000000000, 100, Color.Yellow, new("4F2D1D6B-0ED2-405E-8617-1B5073425F95")),
			new("Schwer+Klein", 1000000000000, 10, Color.Black, Color.White, 2.0d, new("0514F35B-029F-4E91-8071-81FD31C570E0")),
			new("Leicht+Klein", 1000, 20, Color.Blue, new("90424708-FFF6-4BD1-ADAF-6A534BBBACAA")),
			new("Sonne", 1.9884E30d, 696342000.0d, Color.Yellow, new("30584A17-00EE-4B85-ACEB-EFCAF2606468")),
			new("Erde", 5.9724E24d, 12756270.0d / 2, Color.Blue, new("3E9965AB-3A11-414A-A455-50527F254036")),
			new("Mond", 7.346E22d, 3474000.0d / 2, Color.DarkGray, new("71A1DD4C-5B87-405C-8033-B033B46A5237"))
		];

	public IReadOnlyList<EngineType> EngineTypes { get; } =
		[
			new()
			{
				Type = Factory.SimulationEngineType.AdaptiveBarnesHut,
				Name = "Adaptive (Barnes-Hut)"
			},
			new()
			{
				Type = Factory.SimulationEngineType.AdaptiveParticleMesh,
				Name = "Adaptive (Particle-Mesh FFT)"
			},
			new()
			{
				Type = Factory.SimulationEngineType.Standard,
				Name = "Direkte N-Body"
			}
		];

	public Viewport Viewport { get; } = new();

	public TimeSpan Runtime { get; private set; }

	public int CpuUtilizationInPercent { get; private set; }

	public int BodyCount
		=> GetBodies().Length;

	public Guid? CurrentRespawnerId { get; set; }

	public void CreateRandomBodies(int count, bool enableRespawn, bool stableOrbits)
	{
		var viewportSize = Viewport.BottomRight - Viewport.TopLeft;

		for(var i = 0; i < count; i++)
		{
			var position = new Vector3D(_rng.NextDouble() * viewportSize.X, _rng.NextDouble() * viewportSize.Y, _rng.NextDouble() * viewportSize.Z) + Viewport.TopLeft;
			var bodies = GetBodies();

			while(bodies.Any(b => (b.Position - position).Length <= b.r + SelectedBodyPreset.r))
				position = new Vector3D(_rng.NextDouble() * viewportSize.X, _rng.NextDouble() * viewportSize.Y, _rng.NextDouble() * viewportSize.Z) + Viewport.TopLeft;

			if(stableOrbits)
				CreateOrbitBody(position, Vector3D.Zero);
			else
				CreateBody(position, Vector3D.Zero);

			CurrentRespawnerId = enableRespawn
									 ? stableOrbits
										   ? _randomOrbittingRespawnerId
										   : _randomRespawnerId
									 : null;
		}
	}

	public void CreateBody(Vector3D position, Vector3D velocity)
	{
		_bodies.AddLocked(new(position,
							  SelectedBodyPreset.r,
							  SelectedBodyPreset.m,
							  velocity,
							  Vector3D.Zero,
							  SelectedBodyPreset.Color,
							  SelectedBodyPreset.AtmosphereColor,
							  SelectedBodyPreset.AtmosphereThickness));

		RaisePropertyChanged(nameof(BodyCount));
	}

	public void CreateOrbitBody(Vector3D position, Vector3D velocity)
	{
		var nearestBody = SelectedBody
						  ?? GetBodies().OrderByDescending(p => IWorld.G * p.m / ((p.Position - position).Length * (p.Position - position).Length))
										.FirstOrDefault();

		if(null == nearestBody)
		{
			CreateBody(position, Vector3D.Zero);

			return;
		}

		var dist = position - nearestBody.Position;
		var distLen = dist.Length;

		if(distLen < 1e-12)
		{
			CreateBody(position, Vector3D.Zero);

			return;
		}

		var distUnit = dist / distLen;

		// Compute orbit tangent using two cross products
		var up = new Vector3D(0, 0, 1);
		var orbitNormal = distUnit.Cross(up);

		if(orbitNormal.LengthSquared < 1e-12)
		{
			up = new(1, 0, 0);
			orbitNormal = distUnit.Cross(up);
		}

		// Tangent is perpendicular to both orbitNormal and dist (double cross product)
		var tangent = orbitNormal.Cross(distUnit);
		var tangentLen = tangent.Length;

		if(tangentLen < 1e-12)
		{
			CreateBody(position, Vector3D.Zero);

			return;
		}

		tangent = tangent / tangentLen;

		// Determine orbit direction: which tangent direction is closer to velocity?
		// Same logic as the 2D version: compare distance to +tangent vs -tangent
		var velocityUnit = velocity.Length > 1e-12
							   ? velocity / velocity.Length
							   : Vector3D.Zero;
		var direction = (tangent - velocityUnit).Length > (-tangent - velocityUnit).Length
							? -1
							: 1;

		// Calculate orbital velocity for circular orbit: v = sqrt(G * M / r)
		var orbitalSpeed = Math.Sqrt(IWorld.G * nearestBody.m / distLen);

		// The orbital velocity is relative to nearestBody
		// In world frame: v = v_orbit + nearestBody.v
		// This ensures the new body orbits nearestBody while moving with it
		var orbitalVelocity = (1 + velocity.Length) * direction * orbitalSpeed * tangent;
		var v = orbitalVelocity + nearestBody.v;

		CreateBody(position, v);
	}

	public void SelectBody(Point viewportPoint, double viewportSearchRadius)
	{
		// Get picking ray from viewport point
		(var rayOrigin, var rayDir) = Viewport.GetPickingRay(viewportPoint);

		// Search radius only applies perpendicular to the ray (in view plane), not in depth
		var searchRadiusWorld = viewportSearchRadius / Viewport.ScaleFactor;

		Body? closestBody = null;
		var closestDistance = double.MaxValue;

		foreach(var body in GetBodies())
		{
			// Calculate perpendicular distance from ray to body center
			// This is the distance in the view plane, ignoring depth
			var toBody = body.Position - rayOrigin;
			var rayDirDot = rayDir.X * rayDir.X + rayDir.Y * rayDir.Y + rayDir.Z * rayDir.Z;
			var t = (toBody.X * rayDir.X + toBody.Y * rayDir.Y + toBody.Z * rayDir.Z) / rayDirDot;

			// Closest point on ray to body center
			var closestPointOnRay = rayOrigin + t * rayDir;
			var perpDistance = (body.Position - closestPointOnRay).Length;

			// Check if within body radius + search radius (perpendicular to ray only)
			// No depth restriction - orthographic projection shows all depths
			if(perpDistance <= body.r + searchRadiusWorld &&
			   t < closestDistance)
			{
				closestDistance = t;
				closestBody = body;
			}
		}

		SelectedBody = closestBody;
	}

	public void AutoScaleAndCenterViewport()
	{
		var bodies = GetBodies();

		if(bodies.Length == 0)
			return;

		var previousSize = Viewport.Size3D;
		var topLeft = new Vector3D(bodies.Min(e => e.Position.X - e.r),
								   bodies.Min(e => e.Position.Y - e.r),
								   bodies.Min(e => e.Position.Z - e.r));
		var bottomRight = new Vector3D(bodies.Max(e => e.Position.X + e.r),
									   bodies.Max(e => e.Position.Y + e.r),
									   bodies.Max(e => e.Position.Z + e.r));
		var center = topLeft + (bottomRight - topLeft) / 2;
		var newSize = bottomRight - topLeft;
		// Maintain aspect ratio (width:height)
		if(newSize.X / newSize.Y < previousSize.X / previousSize.Y)
			newSize = new(newSize.Y * previousSize.X / previousSize.Y, newSize.Y, newSize.Z);
		if(newSize.X / newSize.Y > previousSize.X / previousSize.Y)
			newSize = new(newSize.X, newSize.X * previousSize.Y / previousSize.X, newSize.Z);

		Viewport.SetBoundsAroundCenter(center, newSize);
		Viewport.Scale += Math.Log10(Math.Max(newSize.X / previousSize.X, newSize.Y / previousSize.Y));
	}

	public void Reset()
	{
		_bodies.ClearLocked();
		Runtime = TimeSpan.Zero;
		SelectedBody = null;
		Viewport.SetBoundsAroundCenter(Vector3D.Zero, Viewport.Size3D);
		Body.ResetIds();
	}

	public async Task SaveAsync(string filePath)
	{
		var state = new State
					{
						Viewport = new(new(Viewport.TopLeft.X, Viewport.TopLeft.Y, Viewport.TopLeft.Z),
									   new(Viewport.BottomRight.X, Viewport.BottomRight.Y, Viewport.BottomRight.Z),
									   Viewport.Scale),
						AutoCenterViewport = AutoCenterViewport,
						ClosedBoundaries = ClosedBoundaries,
						ElasticCollisions = ElasticCollisions,
						ShowPath = ShowPath,
						TimeScale = TimeScale,
						SelectedBodyPresetId = SelectedBodyPreset.Id,
						RespawnerId = CurrentRespawnerId,
						RngState = _rng.State,
						Runtime = Runtime,
						Bodies = GetBodies().Select(b => new State.BodyState(b.Id,
																			 b.Color.ToString(),
																			 b.AtmosphereColor?.ToString(),
																			 b.AtmosphereThickness,
																			 new(b.Position.X, b.Position.Y, b.Position.Z),
																			 new(b.v.X, b.v.Y, b.v.Z),
																			 b.r,
																			 b.m))
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

		var isFlat = state.Viewport.TopLeft.Z.Equals(state.Viewport.BottomRight.Z);
		Viewport.TopLeft = new(state.Viewport.TopLeft.X,
							   state.Viewport.TopLeft.Y,
							   isFlat
								   ? state.Viewport.TopLeft.Y
								   : state.Viewport.TopLeft.Z);
		Viewport.BottomRight = new(state.Viewport.BottomRight.X,
								   state.Viewport.BottomRight.Y,
								   isFlat
									   ? state.Viewport.BottomRight.Y
									   : state.Viewport.BottomRight.Z);
		Viewport.Scale = state.Viewport.Scale;
		AutoCenterViewport = state.AutoCenterViewport;
		ClosedBoundaries = state.ClosedBoundaries;
		ElasticCollisions = state.ElasticCollisions;
		SelectedBodyPreset = BodyPresets.First(p => p.Id == state.SelectedBodyPresetId);
		CurrentRespawnerId = state.RespawnerId;
		ShowPath = state.ShowPath;
		TimeScale = state.TimeScale;
		_rng.State = state.RngState;
		Runtime = state.Runtime;

		_bodies.AddRangeLocked(state.Bodies
									.Select(b => new Body(new(b.Position.X, b.Position.Y, b.Position.Z),
														  b.r,
														  b.m,
														  new(b.v.X, b.v.Y, b.v.Z),
														  Vector3D.Zero,
														  string.IsNullOrEmpty(b.Color)
															  ? Color.Transparent
															  : Color.Parse(b.Color),
														  string.IsNullOrEmpty(b.AtmosphereColor)
															  ? null
															  : Color.Parse(b.AtmosphereColor),
														  b.AtmosphereThickness)));
	}

	public Body[] GetBodies()
		=> _bodies.ToArrayLocked();

	#endregion

	#region Implementation of IWorld

	IViewport IWorld.Viewport
		=> Viewport;

	bool IWorld.ClosedBoundaries
		=> ClosedBoundaries;

	bool IWorld.ElasticCollisions
		=> ElasticCollisions;

	double IWorld.TimeScaleFactor
		=> TimeScaleFactor;

	Body[] IWorld.GetBodies()
		=> GetBodies();

	#endregion

	#region Implementation

	private async Task SimulateAsync()
		=> await Task.Run(Simulate);

	private void Simulate()
	{
		if(null == _simulationEngine ||
		   1 == Interlocked.CompareExchange(ref _isSimulating, 1, 0))
			return;

		using var exit = new DisposableAction(() =>
											  {
												  var frameDiagnosticsMeasurement = _frameTiming.LastMeasurement;

												  CpuUtilizationInPercent = (int)Math.Round(frameDiagnosticsMeasurement.CpuUtilizationEmaInPercent);
												  FramesPerSecond = (int)Math.Round(1000.0d / frameDiagnosticsMeasurement.LastFrameDurationInMs);

												  // Log every ~60 frames; use TraceInformation for explicit event type
												  if(frameDiagnosticsMeasurement.FrameCount % 60 == 0)
												  {
													  var engineDiagnostics = string.Join(" | ", _simulationEngine!.GetDiagnostics()
																												   .Fields
																												   .Select(p => $"{p.Key}: {p.Value}"));

													  Trace.TraceInformation($"Frame: {frameDiagnosticsMeasurement.LastFrameDurationInMs:F1} ms" +
																			 $" | CPU: {frameDiagnosticsMeasurement.CpuUtilizationInPercent}%" +
																			 $" | Bodies: {BodyCount}" +
																			 $" | AllocDelta: {frameDiagnosticsMeasurement.DeltaAllocated / (1024.0 * 1024.0):F2} MiB" +
																			 (string.IsNullOrEmpty(engineDiagnostics)
																				  ? string.Empty
																				  : $" | {engineDiagnostics}"));
												  }

												  _isSimulating = 0;
											  });

		using var measure = _frameTiming.Measure();

		if(!IsRunning)
			return;

		var now = _simulationTime.Elapsed;

		if(_lastSimulationStep == TimeSpan.Zero)
		{
			_lastSimulationStep = now;

			return;
		}

		var deltaTime = (now - _lastSimulationStep) * TimeScaleFactor;

		_lastSimulationStep = now;

		UpdateAllBodies(deltaTime);

		if(AutoCenterViewport)
			DoAutoCenterViewport();

		Runtime += deltaTime;
	}

	private void UpdateAllBodies(TimeSpan deltaTime)
	{
		var bodies = GetBodies();

		if(bodies.Length == 0 ||
		   _simulationEngine == null)
			return;

		_simulationEngine.Simulate(this, deltaTime);

		// DEBUG: Check if acceleration points towards or away from center of mass
		//var runtimeSec = Runtime.TotalSeconds;
		//if(bodies.Length > 1)
		//{
		//	var activeBodies = bodies.Where(b => !b.IsAbsorbed).ToArray();
		//	if(activeBodies.Length > 1)
		//	{
		//		var totalMass = activeBodies.Sum(b => b.m);
		//		var com = new Vector3D(
		//			activeBodies.Sum(b => b.m * b.Position.X) / totalMass,
		//			activeBodies.Sum(b => b.m * b.Position.Y) / totalMass,
		//			activeBodies.Sum(b => b.m * b.Position.Z) / totalMass);

		//		var invalidBodies = activeBodies.Select(b =>
		//											  {
		//												  var toCenter = com - b.Position;
		//												  var toCenterLen = toCenter.Length;
		//												  var aLen = b.a.Length;

		//												  if(toCenterLen <= 1e-6 ||
		//													 aLen <= 1e-12)
		//													  return new
		//															 {
		//																 Body = b,
		//																 Dot = double.NaN,
		//																 ALen = aLen,
		//																 DistToCenter = toCenterLen
		//															 };

		//												  var toCenterUnit = toCenter / toCenterLen;
		//												  var aUnit = b.a / aLen;
		//												  var dot = toCenterUnit.X * aUnit.X + toCenterUnit.Y * aUnit.Y + toCenterUnit.Z * aUnit.Z;

		//												  return new
		//														 {
		//															 Body = b,
		//															 Dot = dot,
		//															 ALen = aLen,
		//															 DistToCenter = toCenterLen
		//														 };
		//											  })
		//									  // dot > 0 means acceleration towards center (correct)
		//									  // dot < 0 means acceleration away from center (BUG!)
		//									  .Where(t => !double.IsNaN(t.Dot) && t.Dot < 0)
		//									  .ToArray();

		//		var invalidBody = invalidBodies.FirstOrDefault();

		//		if (null!=invalidBody)
		//			Trace.TraceWarning($"[T={runtimeSec:F1}s] " +
		//							   $"Body {invalidBody.Body.Id} (Total: {invalidBodies.Length}): " +
		//							   $"accel AWAY FROM ({com.X},{com.Y},{com.Z}), " +
		//							   $"dot={invalidBody.Dot:F3}, " +
		//							   $"m={invalidBody.Body.m:E3}, " +
		//							   $"|a|={invalidBody.ALen:E3}, " +
		//							   $"distToCenter={invalidBody.DistToCenter:E3}");
		//	}
		//}

		var respawner = CurrentRespawnerId.HasValue
							? _respawnersById[CurrentRespawnerId.Value]
							: null;

		var absorbedBodies = bodies.Where(b => b.IsAbsorbed).ToArray();

		// Absorbierte Objekte entfernen
		_bodies.RemoveRangeLocked(absorbedBodies);

		// Bei Bedarf Respawner aufrufen
		if(null == respawner)
			return;

		foreach(var _ in absorbedBodies)
			respawner();
	}

	private void DoAutoCenterViewport()
	{
		var bodies = GetBodies();

		if(bodies.Length == 0)
			return;

		var topLeft = new Vector3D(bodies.Min(b => b.Position.X - b.r),
								   bodies.Min(b => b.Position.Y - b.r),
								   bodies.Min(b => b.Position.Z - b.r));
		var bottomRight = new Vector3D(bodies.Max(b => b.Position.X + b.r),
									   bodies.Max(b => b.Position.Y + b.r),
									   bodies.Max(b => b.Position.Z + b.r));
		var center = topLeft + (bottomRight - topLeft) / 2;

		Viewport.SetCenter(center);
	}

	#endregion
}