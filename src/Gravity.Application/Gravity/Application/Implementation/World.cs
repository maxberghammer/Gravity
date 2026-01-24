using System;
using System.Collections.Generic;
using System.Linq;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;
using Wellenlib.Collections.Generic;

namespace Gravity.Application.Gravity.Application.Implementation;

internal sealed class World : IWorld,
							  IApplication.IWorld
{
	#region Fields

	private readonly ConcurrentList<Body> _bodies = [];
	private bool _closedBoundaries;
	private bool _elasticCollisions;
	private double _timescale;

	#endregion

	#region Interface

	public int BodyCount
		=> _bodies.Count;

	public State.WorldState GetState()
		=> new(_elasticCollisions,
			   _closedBoundaries,
			   _timescale,
			   _bodies.Select(b => new State.BodyState(b.Id,
													   b.Color.ToString(),
													   b.AtmosphereColor?.ToString(),
													   b.AtmosphereThickness,
													   new(b.Position.X, b.Position.Y, b.Position.Z),
													   new(b.v.X, b.v.Y, b.v.Z),
													   b.r,
													   b.m))
					  .ToArray());

	public void ApplyState(State.WorldState state)
	{
		_elasticCollisions = state.ElasticCollisions;
		_closedBoundaries = state.ClosedBoundaries;
		_timescale = state.Timescale;
		_bodies.AddRange(state.Bodies
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

	public IReadOnlyList<Body> GetBodies()
		=> _bodies.AsReadOnly();

	public void AddBody(Body body)
		=> _bodies.Add(body);

	public TimeSpan ToWorld(TimeSpan applicationTimeSpan)
		=> applicationTimeSpan * _timescale;

	public void Reset()
	{
		_bodies.Clear();

		Body.Reset();
	}

	#endregion

	#region Implementation of IWorld

	/// <inheritdoc/>
	double IWorld.Timescale
		=> _timescale;

	/// <inheritdoc/>
	bool IWorld.ClosedBoundaries
		=> _closedBoundaries;

	/// <inheritdoc/>
	bool IWorld.ElasticCollisions
		=> _elasticCollisions;

	/// <inheritdoc/>
	int IApplication.IWorld.BodyCount
		=> BodyCount;

	/// <inheritdoc/>
	IReadOnlyList<Body> IApplication.IWorld.GetBodies()
		=> GetBodies();

	/// <inheritdoc/>
	void IWorld.RemoveBodies(IReadOnlyCollection<Body> bodies)
		=> _bodies.RemoveRange(bodies);

	/// <inheritdoc/>
	IReadOnlyList<Body> IWorld.GetBodies()
		=> GetBodies();

	/// <inheritdoc/>
	TimeSpan IApplication.IWorld.ToWorld(TimeSpan applicationTimeSpan)
		=> ToWorld(applicationTimeSpan);

	/// <inheritdoc/>
	TimeSpan IApplication.IWorld.ToApplication(TimeSpan worldTimeSpan)
		=> worldTimeSpan / _timescale;

	/// <inheritdoc/>
	void IApplication.IWorld.SetTimescale(double timeScale)
		=> _timescale = timeScale;

	/// <inheritdoc/>
	void IApplication.IWorld.EnableElasticCollisions()
		=> _elasticCollisions = true;

	/// <inheritdoc/>
	void IApplication.IWorld.DisableElasticCollisions()
		=> _elasticCollisions = false;

	/// <inheritdoc/>
	void IApplication.IWorld.EnableClosedBoundaries()
		=> _closedBoundaries = true;

	/// <inheritdoc/>
	void IApplication.IWorld.DisableClosedBoundaries()
		=> _closedBoundaries = false;

	#endregion
}