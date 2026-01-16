using System;
using System.Linq;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class SimulationEngine : ISimulationEngine
{
	#region Internal types

	internal interface IIntegrator
	{
		void Step(IWorld world, Body[] bodies, double dtInSeconds, Action<Body[]> computation, Diagnostics diagnostics);
	}

	internal interface IOversampler
	{
		int Oversample(IWorld world, Body[] bodies, TimeSpan timeSpan, Action<Body[], TimeSpan> processBodies, Diagnostics diagnostics);
	}

	internal interface IComputation
	{
		void Compute(IWorld world, Body[] bodies, Diagnostics diagnostics);
	}

	internal interface ICollisionResolver
	{
		void ResolveCollisions(IWorld world, Body[] bodies, Diagnostics diagnostics);
	}

	#endregion

	#region Fields

	private readonly ICollisionResolver _collisionResolver;
	private readonly IComputation _computation;
	private readonly Diagnostics _diagnostics = new();
	private readonly IIntegrator _integrator;
	private readonly IOversampler _oversampler;

	#endregion

	#region Construction

	public SimulationEngine(IIntegrator integrator, IOversampler oversampler, IComputation computation, ICollisionResolver collisionResolver)
	{
		_integrator = integrator;
		_oversampler = oversampler;
		_computation = computation;
		_collisionResolver = collisionResolver;
	}

	#endregion

	#region Implementation of ISimulationEngine

	ISimulationEngine.IDiagnostics ISimulationEngine.GetDiagnostics()
		=> _diagnostics;

	void ISimulationEngine.Simulate(IWorld world, TimeSpan deltaTime)
	{
		var bodies = world.GetBodies();

		if(bodies.Length == 0 ||
		   deltaTime <= TimeSpan.Zero)
			return;

		var steps = _oversampler.Oversample(world,
											bodies,
											deltaTime,
											(b, dt) =>
											{
												_integrator.Step(world,
																 b,
																 dt.TotalSeconds,
																 bs => _computation.Compute(world, bs, _diagnostics),
																 _diagnostics);

												_collisionResolver.ResolveCollisions(world, bodies, _diagnostics);
											},
											_diagnostics);

		// Report oversampling for diagnostics
		_diagnostics.SetField("Oversampling",
							  steps == 1
								  ? "Off"
								  : $"{steps}x");

		if(!world.ClosedBoundaries)
			return;

		// Weltgrenzen behandeln
		foreach(var body in bodies.Where(e => !e.IsAbsorbed))
			HandleCollisionWithWorldBoundaries(world, body);
	}

	#endregion

	#region Implementation

	/// <summary>
	///     Behandelt die Kollision eines gegebenen Objekts mit den Grenzen der Welt.
	/// </summary>
	private static void HandleCollisionWithWorldBoundaries(IWorld world, Body body)
	{
		var leftX = world.Viewport.TopLeft.X + body.r;
		var topY = world.Viewport.TopLeft.Y + body.r;
		var rightX = world.Viewport.BottomRight.X - body.r;
		var bottomY = world.Viewport.BottomRight.Y - body.r;

		var pos = body.Position;
		var v = body.v;

		if(pos.X < leftX)
		{
			v = new(-v.X, v.Y);
			pos = new(leftX, pos.Y);
		}
		else if(pos.X > rightX)
		{
			v = new(-v.X, v.Y);
			pos = new(rightX, pos.Y);
		}

		if(pos.Y < topY)
		{
			v = new(v.X, -v.Y);
			pos = new(pos.X, topY);
		}
		else if(pos.Y > bottomY)
		{
			v = new(v.X, -v.Y);
			pos = new(pos.X, bottomY);
		}

		body.v = v;
		body.Position = pos;
	}

	#endregion
}