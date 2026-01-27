using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;
using Wellenlib;
using Wellenlib.Diagnostics;

namespace Gravity.Application.Gravity.Application.Implementation;

internal sealed class Application : IApplication,
									IDisposable
{
	#region Fields

	private static readonly Guid _randomOrbittingRespawnerId = new("F02C36A4-FEC2-49AD-B3DA-C7E9B6E4C361");
	private static readonly Guid _randomRespawnerId = new("7E4948F8-CFA5-45A3-BB05-48CB4AAB13B1");

	private readonly IReadOnlyList<IApplication.BodyPreset> _bodyPresets =
	[
		IApplication.BodyPreset.FromDensity("Eisenkugel klein", 7874, 10, Color.DarkGray, Color.White, 2.0d, new("C53FA0C5-AB12-43F7-9548-C098D5C44ADF")),
		IApplication.BodyPreset.FromDensity("Eisenkugel mittel", 7874, 20, Color.DarkGray, Color.White, 2.0d,
											new("CB30E40F-FB49-4688-94D1-3F1FB5C3F813")),
		IApplication.BodyPreset.FromDensity("Eisenkugel groß", 7874, 100, Color.DarkGray, Color.White, 2.0d, new("03F7274E-B6C5-46E8-B8F8-03C969C79B49")),
		new("Mittelschwer+Klein", 100000000000, 20, Color.Green, new("98E60B8E-4461-4895-9107-A1FF5C9B9D64")),
		new("Leicht+Groß", 1000000000, 100, Color.Red, new("B6BBB8AC-109C-4CA1-96E1-976EABED256E")),
		new("Schwer+Groß", 1000000000000, 100, Color.Yellow, new("4F2D1D6B-0ED2-405E-8617-1B5073425F95")),
		new("Schwer+Klein", 1000000000000, 10, Color.Black, Color.White, 2.0d, new("0514F35B-029F-4E91-8071-81FD31C570E0")),
		new("Leicht+Klein", 1000, 20, Color.Blue, new("90424708-FFF6-4BD1-ADAF-6A534BBBACAA")),
		new("Sonne", 1.9884E30d, 696342000.0d, Color.Yellow, new("30584A17-00EE-4B85-ACEB-EFCAF2606468")),
		new("Erde", 5.9724E24d, 12756270.0d / 2, Color.Blue, new("3E9965AB-3A11-414A-A455-50527F254036")),
		new("Mond", 7.346E22d, 3474000.0d / 2, Color.DarkGray, new("71A1DD4C-5B87-405C-8033-B033B46A5237"))
	];

	private readonly IReadOnlyList<IApplication.EngineType> _engineTypes =
	[
		new()
		{
			Type = Factory.SimulationEngineType.HierarchicalBlockDirect,
			Name = "Hierarchical Block (Direct N-Body)"
		},
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
			Type = Factory.SimulationEngineType.AdaptiveFastMultipole,
			Name = "Adaptive (Fast Multipole)"
		},
		new()
		{
			Type = Factory.SimulationEngineType.Standard,
			Name = "Direkte N-Body"
		}
	];

	private readonly FrameDiagnostics _frameTiming = new();
	private readonly Dictionary<Guid, Action> _respawnersById = new();
	private readonly SimpleRng _rng = new();
	private readonly Stopwatch _simulationTime = new();
	private readonly Viewport _viewport;
	private readonly World _world = new();
	private IApplication.ApplyStateHandler? _applyState;
	private Guid? _currentRespawnerId;
	private int _isSimulating;
	private TimeSpan _lastSimulationStep = TimeSpan.Zero;
	private TimeSpan _runtime;
	private Body? _selectedBody;
	private IApplication.BodyPreset _selectedBodyPreset;
	private ISimulationEngine? _simulationEngine;
	private Timer? _simulationTimer;
	private IApplication.UpdateStateHandler? _updateState;

	#endregion

	#region Construction

	public Application()
	{
		//_simulationTime.Start();

		_viewport = new(_world);
		_selectedBodyPreset = _bodyPresets[0];
		_simulationEngine = Factory.Create(_engineTypes[0].Type);
		_respawnersById[_randomRespawnerId] = () => AddRandomBodies(1, true, false);
		_respawnersById[_randomOrbittingRespawnerId] = () => AddRandomBodies(1, true, true);
	}

	#endregion

	#region Implementation of IApplication

	/// <inheritdoc/>
	event IApplication.ApplyStateHandler? IApplication.ApplyState { add => _applyState += value; remove => _applyState -= value; }

	/// <inheritdoc/>
	event IApplication.UpdateStateHandler? IApplication.UpdateState { add => _updateState += value; remove => _updateState -= value; }

	/// <inheritdoc/>
	public IApplication.IWorld World
		=> _world;

	/// <inheritdoc/>
	public IApplication.IViewport Viewport
		=> _viewport;

	/// <inheritdoc/>
	TimeSpan IApplication.Runtime
		=> _runtime;

	/// <inheritdoc/>
	IReadOnlyList<IApplication.BodyPreset> IApplication.BodyPresets
		=> _bodyPresets;

	/// <inheritdoc/>
	IReadOnlyList<IApplication.EngineType> IApplication.EngineTypes
		=> _engineTypes;

	/// <inheritdoc/>
	FrameDiagnostics IApplication.FrameDiagnostics
		=> _frameTiming;

	/// <inheritdoc/>
	void IApplication.AddBody(Vector3D position, Vector3D velocity)
		=> AddBody(position, velocity);

	/// <inheritdoc/>
	void IApplication.AddOrbitBody(Vector3D position, Vector3D velocity)
		=> AddOrbitBody(position, velocity);

	/// <inheritdoc/>
	void IApplication.AddRandomBodies(int count, bool enableRespawn, bool stableOrbits)
		=> AddRandomBodies(count, enableRespawn, stableOrbits);

	/// <inheritdoc/>
	void IApplication.StopRespawn()
		=> _currentRespawnerId = null;

	/// <inheritdoc/>
	void IApplication.StartSimulation()
		=> _simulationTime.Start();

	/// <inheritdoc/>
	void IApplication.StopSimulation()
		=> _simulationTime.Stop();

	/// <inheritdoc/>
	void IApplication.Reset()
		=> Reset();

	/// <inheritdoc/>
	Body? IApplication.FindClosestBody(Vector2 viewportPoint, double viewportSearchRadius)
	{
		// Get picking ray from viewport point
		(var rayOrigin, var rayDir) = _viewport.GetPickingRay(viewportPoint);

		// Search radius only applies perpendicular to the ray (in view plane), not in depth
		var searchRadiusWorld = viewportSearchRadius / _viewport.ScaleFactor;

		Body? closestBody = null;
		var closestDistance = double.MaxValue;

		foreach(var body in _world.GetBodies())
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

		return closestBody;
	}

	/// <inheritdoc/>
	void IApplication.Select(IApplication.BodyPreset bodyPreset)
		=> _selectedBodyPreset = bodyPreset;

	/// <inheritdoc/>
	void IApplication.Select(IApplication.EngineType engineType)
		=> _simulationEngine = Factory.Create(engineType.Type);

	/// <inheritdoc/>
	void IApplication.Select(Body? body)
		=> _selectedBody = body;

	/// <inheritdoc/>
	void IApplication.SetSimulationFrequency(double frequencyInHz)
	{
		_simulationTimer?.Dispose();

		var intervalMs = (int)(1000.0 / frequencyInHz);

		_simulationTimer = new(_ => Simulate(), null, intervalMs, intervalMs);
	}

	/// <inheritdoc/>
	async Task IApplication.SaveAsync(string filePath)
	{
		var state = new State
					{
						Viewport = _viewport.GetState(),
						World = _world.GetState(),
						SelectedBodyPresetId = _selectedBodyPreset.Id,
						RespawnerId = _currentRespawnerId,
						RngState = _rng.State,
						Runtime = _runtime
					};

		if(null != _updateState)
			state = _updateState(state);

		await using var swr = File.CreateText(filePath);
		await state.SerializeAsync(swr);
	}

	/// <inheritdoc/>
	async Task IApplication.OpenAsync(string filePath)
	{
		using var srd = File.OpenText(filePath);
		var state = await State.DeserializeAsync(srd);

		Reset();

		_viewport.ApplyState(state.Viewport);
		_world.ApplyState(state.World);

		_selectedBodyPreset = _bodyPresets.FirstOrDefault(p => p.Id == state.SelectedBodyPresetId) 
							  ?? _bodyPresets[0];
		_currentRespawnerId = state.RespawnerId;
		_rng.State = state.RngState;
		_runtime = state.Runtime;

		_applyState?.Invoke(state);
	}

	#endregion

	#region Implementation of IDisposable

	/// <inheritdoc/>
	void IDisposable.Dispose()
		=> _simulationTimer?.Dispose();

	#endregion

	#region Implementation

	private void Reset()
	{
		_world.Reset();
		_viewport.Reset();
		_runtime = TimeSpan.Zero;
		_selectedBody = null;
		_simulationTime.Reset();
		_lastSimulationStep = TimeSpan.Zero;
	}

	private void AddBody(Vector3D position, Vector3D velocity)
		=> _world.AddBody(new(position,
							  _selectedBodyPreset.r,
							  _selectedBodyPreset.m,
							  velocity,
							  Vector3D.Zero,
							  _selectedBodyPreset.Color,
							  _selectedBodyPreset.AtmosphereColor,
							  _selectedBodyPreset.AtmosphereThickness,
							  null));

	private void AddOrbitBody(Vector3D position, Vector3D velocity)
	{
		var nearestBody = _selectedBody
						  ?? _world.GetBodies()
								   .OrderByDescending(p => IWorld.G * p.m / ((p.Position - position).Length * (p.Position - position).Length))
								   .FirstOrDefault();

		if(null == nearestBody)
		{
			AddBody(position, Vector3D.Zero);

			return;
		}

		var dist = position - nearestBody.Position;
		var distLen = dist.Length;

		if(distLen < 1e-12)
		{
			AddBody(position, Vector3D.Zero);

			return;
		}

		var distUnit = dist / distLen;

		// Calculate tangent perpendicular to dist and in the view plane (perpendicular to camera forward)
		// This ensures the orbit is in the plane the user is currently viewing
		var cameraForward = _viewport.GetCameraForward();
		var tangent = cameraForward.Cross(distUnit);
		var tangentLen = tangent.Length;

		if(tangentLen < 1e-12)
		{
			// Fallback: dist is parallel to camera forward, use arbitrary perpendicular
			tangent = new(-distUnit.Y, distUnit.X, 0);
			tangentLen = tangent.Length;

			if(tangentLen < 1e-12)
			{
				tangent = new(1, 0, 0);
				tangentLen = 1;
			}
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

		AddBody(position, v);
	}

	private void AddRandomBodies(int count, bool enableRespawn, bool stableOrbits)
	{
		var viewportSize = _viewport.BottomRight - _viewport.TopLeft;

		for(var i = 0; i < count; i++)
		{
			var position = new Vector3D(_rng.NextDouble() * viewportSize.X, _rng.NextDouble() * viewportSize.Y, _rng.NextDouble() * viewportSize.Z) + _viewport.TopLeft;
			var bodies = _world.GetBodies();

			while(bodies.Any(b => (b.Position - position).Length <= b.r + _selectedBodyPreset.r))
				position = new Vector3D(_rng.NextDouble() * viewportSize.X, _rng.NextDouble() * viewportSize.Y, _rng.NextDouble() * viewportSize.Z) + _viewport.TopLeft;

			if(stableOrbits)
				AddOrbitBody(position, Vector3D.Zero);
			else
				AddBody(position, Vector3D.Zero);

			_currentRespawnerId = enableRespawn
									  ? stableOrbits
											? _randomOrbittingRespawnerId
											: _randomRespawnerId
									  : null;
		}
	}

	private void Simulate()
	{
		if(null == _simulationEngine ||
		   1 == Interlocked.CompareExchange(ref _isSimulating, 1, 0))
			return;

		using var exit = new DisposableAction(() =>
											  {
												  var frameDiagnosticsMeasurement = _frameTiming.LastMeasurement;

												  // Log every ~60 frames; use TraceInformation for explicit event type
												  if(frameDiagnosticsMeasurement.FrameCount % 60 == 0)
												  {
													  var engineDiagnostics = string.Join(" | ", _simulationEngine!.GetDiagnostics()
																												   .Fields
																												   .Select(p => $"{p.Key}: {p.Value}"));

													  Trace.TraceInformation($"Frame: {frameDiagnosticsMeasurement.LastFrameDurationInMs:F1} ms" +
																			 $" | CPU: {frameDiagnosticsMeasurement.CpuUtilizationInPercent}%" +
																			 $" | Bodies: {_world.BodyCount}" +
																			 $" | AllocDelta: {frameDiagnosticsMeasurement.DeltaAllocated / (1024.0 * 1024.0):F2} MiB" +
																			 (string.IsNullOrEmpty(engineDiagnostics)
																				  ? string.Empty
																				  : $" | {engineDiagnostics}"));
												  }

												  _isSimulating = 0;
											  });

		using var measure = _frameTiming.Measure();

		if(!_simulationTime.IsRunning)
			return;

		var now = _simulationTime.Elapsed;

		if(_lastSimulationStep == TimeSpan.Zero)
		{
			_lastSimulationStep = now;

			return;
		}

		var deltaTime = _world.ToWorld(now - _lastSimulationStep);

		_lastSimulationStep = now;

		UpdateAllBodies(deltaTime);

		if(_viewport.Autocenter)
			DoAutoCenterViewport();

		_runtime += deltaTime;
	}

	private void UpdateAllBodies(TimeSpan deltaTime)
	{
		var bodies = _world.GetBodies();

		if(bodies.Count == 0 ||
		   _simulationEngine == null)
			return;

		_simulationEngine.Simulate(_world, _viewport, deltaTime);

		var respawner = _currentRespawnerId.HasValue
							? _respawnersById[_currentRespawnerId.Value]
							: null;

		// Bei Bedarf Respawner aufrufen
		if(null == respawner)
			return;

		var absorbedBodyCount = bodies.Count - _world.GetBodies().Count;

		for(var i = 0; i < absorbedBodyCount; i++)
			respawner();
	}

	private void DoAutoCenterViewport()
	{
		var bodies = _world.GetBodies();

		if(bodies.Count == 0)
			return;

		var topLeft = new Vector3D(bodies.Min(b => b.Position.X - b.r),
								   bodies.Min(b => b.Position.Y - b.r),
								   bodies.Min(b => b.Position.Z - b.r));
		var bottomRight = new Vector3D(bodies.Max(b => b.Position.X + b.r),
									   bodies.Max(b => b.Position.Y + b.r),
									   bodies.Max(b => b.Position.Z + b.r));
		var center = topLeft + (bottomRight - topLeft) / 2;

		_viewport.SetCenter(center);
	}

	#endregion
}