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

	private readonly ConcurrentDictionary<int, Vector2D> _positionByEntityId = new();
	private readonly ConcurrentDictionary<int, Vector2D> _vByEntityId = new();

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnSimulate(IWorld world, Body[] entities, TimeSpan deltaTime)
	{
		// Physik anwenden
		ApplyPhysics(world,
					 entities.Where(e => !e.IsAbsorbed)
							 .ToArray());

		// Objekte updaten
		foreach(var entity in entities)
			Update(entity,
				   _positionByEntityId.TryGetValue(entity.Id, out var position)
					   ? position
					   : null,
				   _vByEntityId.TryGetValue(entity.Id, out var v)
					   ? v
					   : null,
				   deltaTime);
	}

	private static void Update(Body entity, Vector2D? position, Vector2D? v, TimeSpan deltaTime)
	{
		if(entity.IsAbsorbed)
			return;

		// Bei Bedarf neue Position übernehmen
		if(position.HasValue)
			entity.Position = position.Value;

		// Position aktualisieren
		entity.Position += entity.v * deltaTime.TotalSeconds;

		// Bei Bedarf neue Geschwindigkeit übernehmen
		if(v.HasValue)
			entity.v = v.Value;

		// Geschwindigkeit aktualisieren
		entity.v += entity.a * deltaTime.TotalSeconds;
	}

	private void ApplyPhysics(IWorld world, Body entity, IEnumerable<Body> others)
	{
		if(entity.IsAbsorbed)
			return;

		entity.a = Vector2D.Zero;

		var g = Vector2D.Zero;

		foreach(var other in others.Where(e => !e.IsAbsorbed))
		{
			// Kollision behandeln
			(var v1, var v2) = HandleCollision(entity, other, world.ElasticCollisions);

			if(v1.HasValue &&
			   v2.HasValue)
			{
				(var position1, var position2) = CancelOverlap(entity, other);

				if(position1.HasValue)
					_positionByEntityId[entity.Id] = position1.Value;

				if(position2.HasValue)
					_positionByEntityId[other.Id] = position2.Value;
			}

			if(v1.HasValue)
				_vByEntityId[entity.Id] = v1.Value;

			if(v2.HasValue)
				_vByEntityId[other.Id] = v2.Value;

			if(entity.IsAbsorbed)
				return;

			var dist = entity.Position - other.Position;

			// Gravitationsbeschleunigung integrieren
			g += other.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
		}

		entity.a = -IWorld.G * g;
	}

	private void ApplyPhysics(IWorld world, Body[] entities)
	{
		_positionByEntityId.Clear();
		_vByEntityId.Clear();

		var n = entities.Length;
		var chunk = Math.Max(1, IWorld.GetPreferredChunkSize(entities));
		var chunks = (n + chunk - 1) / chunk;

		Parallel.For(0, chunks, c =>
								{
									var start = c * chunk;
									var end = Math.Min(start + chunk, n);

									for(var i = start; i < end; i++)
										ApplyPhysics(world, entities[i], entities.Except(entities[i]));
								});
	}

	#endregion
}