// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class StandardSimulationEngine : ISimulationEngine
{
	#region Fields

	// ReSharper disable once InconsistentNaming
	private readonly ConcurrentDictionary<int, Vector2D> _positionByEntityId = new();

	// ReSharper disable once InconsistentNaming
	private readonly ConcurrentDictionary<int, Vector2D> _vByEntityId = new();

	#endregion

	#region Implementation of ISimulationEngine

	/// <inheritdoc/>
	async Task ISimulationEngine.SimulateAsync(Entity[] entities, TimeSpan deltaTime)
	{
		// Physik anwenden
		await ApplyPhysicsAsync(entities.Where(e => !e.IsAbsorbed)
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

	#endregion

	#region Implementation

	// ReSharper disable InconsistentNaming
	private static void Update(Entity entity, Vector2D? position, Vector2D? v, TimeSpan deltaTime)
		// ReSharper restore InconsistentNaming
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

		if(entity.World.ClosedBoundaries)
			entity.HandleCollisionWithWorldBoundaries();
	}

	private void ApplyPhysics(Entity entity, IEnumerable<Entity> others)
	{
		if(entity.IsAbsorbed)
			return;

		entity.a = Vector2D.Zero;

		var g = Vector2D.Zero;

		foreach(var other in others.Where(e => !e.IsAbsorbed))
		{
			// Kollision behandeln
			(var v1, var v2) = entity.HandleCollision(other, entity.World.ElasticCollisions);

			if(v1.HasValue &&
			   v2.HasValue)
			{
				(var position1, var position2) = entity.CancelOverlap(other);

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

	private async Task ApplyPhysicsAsync(IReadOnlyCollection<Entity> entities)
	{
		_positionByEntityId.Clear();
		_vByEntityId.Clear();

		await Task.WhenAll(entities.Chunked(IWorld.GetPreferredChunkSize(entities))
								   .Select(chunk => Task.Run(() =>
															 {
																 foreach(var entity in chunk)
																	 ApplyPhysics(entity, entities.Except(entity));
															 })));
	}

	#endregion
}