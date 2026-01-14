// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Standard;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Fields

	private readonly ConcurrentDictionary<int, Vector2D> _positionByBodyId = new();
	private readonly ConcurrentDictionary<int, Vector2D> _vByBodyId = new();

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnSimulate(IWorld world, Body[] bodies, TimeSpan deltaTime)
	{
		// Physik anwenden
		ApplyPhysics(world,
					 bodies.Where(e => !e.IsAbsorbed)
							 .ToArray());

		// Objekte updaten
		foreach(var body in bodies)
			Update(body,
				   _positionByBodyId.TryGetValue(body.Id, out var position)
					   ? position
					   : null,
				   _vByBodyId.TryGetValue(body.Id, out var v)
					   ? v
					   : null,
				   deltaTime);
	}

	private static void Update(Body body, Vector2D? position, Vector2D? v, TimeSpan deltaTime)
	{
		if(body.IsAbsorbed)
			return;

		// Bei Bedarf neue Position übernehmen
		if(position.HasValue)
			body.Position = position.Value;

		// Position aktualisieren
		body.Position += body.v * deltaTime.TotalSeconds;

		// Bei Bedarf neue Geschwindigkeit übernehmen
		if(v.HasValue)
			body.v = v.Value;

		// Geschwindigkeit aktualisieren
		body.v += body.a * deltaTime.TotalSeconds;
	}

	private void ApplyPhysics(IWorld world, Body body, IEnumerable<Body> others)
	{
		if(body.IsAbsorbed)
			return;

		body.a = Vector2D.Zero;

		var g = Vector2D.Zero;

		foreach(var other in others.Where(e => !e.IsAbsorbed))
		{
			// Kollision behandeln
			(var v1, var v2) = HandleCollision(body, other, world.ElasticCollisions);

			if(v1.HasValue &&
			   v2.HasValue)
			{
				(var position1, var position2) = CancelOverlap(body, other);

				if(position1.HasValue)
					_positionByBodyId[body.Id] = position1.Value;

				if(position2.HasValue)
					_positionByBodyId[other.Id] = position2.Value;
			}

			if(v1.HasValue)
				_vByBodyId[body.Id] = v1.Value;

			if(v2.HasValue)
				_vByBodyId[other.Id] = v2.Value;

			if(body.IsAbsorbed)
				return;

			var dist = body.Position - other.Position;

			// Gravitationsbeschleunigung integrieren
			g += other.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
		}

		body.a = -IWorld.G * g;
	}

	private void ApplyPhysics(IWorld world, Body[] bodies)
	{
		_positionByBodyId.Clear();
		_vByBodyId.Clear();

		var n = bodies.Length;
		var chunk = Math.Max(1, IWorld.GetPreferredChunkSize(bodies));
		var chunks = (n + chunk - 1) / chunk;

		Parallel.For(0, chunks, c =>
								{
									var start = c * chunk;
									var end = Math.Min(start + chunk, n);

									for(var i = start; i < end; i++)
										ApplyPhysics(world, bodies[i], bodies.Except(bodies[i]));
								});
	}

	#endregion
}