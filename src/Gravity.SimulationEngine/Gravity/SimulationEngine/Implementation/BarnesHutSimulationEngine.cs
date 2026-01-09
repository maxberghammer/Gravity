// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class BarnesHutSimulationEngine : ISimulationEngine
{
	#region Fields

	private readonly IIntegrator _integrator = new NullIntegrator();

	#endregion

	#region Implementation of ISimulationEngine

	/// <inheritdoc/>
	async Task ISimulationEngine.SimulateAsync(Entity[] entities, TimeSpan deltaTime)
	{
		var collisions = await _integrator.IntegrateAsync(entities, deltaTime, async es => await ApplyPhysicsAsync(es));

		if (collisions.Length != 0)
		{
			var entitiesById = new Dictionary<int, Entity>(entities.Length);
			for (int i = 0; i < entities.Length; i++)
			{
				entitiesById[entities[i].Id] = entities[i];
			}

			// Deduplicate collision pairs
			var seen = new HashSet<(int, int)>();
			var keys = new List<(int, int)>(collisions.Length);
			for (int i = 0; i < collisions.Length; i++)
			{
				var a = collisions[i].Item1;
				var b = collisions[i].Item2;
				var key = a < b ? (a, b) : (b, a);
				if (seen.Add(key)) keys.Add(key);
			}

			if (keys.Count > 0)
			{
				// Compute collision effects in parallel (pure computation), then apply results sequentially
				var effects = new (Vector2D? p1, Vector2D? p2, Vector2D? v1, Vector2D? v2)[keys.Count];
				var chunkSize = GetTunedChunkSize(keys.Count);
				var rangeCount = (keys.Count + chunkSize - 1) / chunkSize;
				Parallel.For(0, rangeCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, rangeIndex =>
				{
					var start = rangeIndex * chunkSize;
					var end = Math.Min(start + chunkSize, keys.Count);
					for (int i = start; i < end; i++)
					{
						var key = keys[i];
						if (!entitiesById.TryGetValue(key.Item1, out var entity1)) { effects[i] = (null, null, null, null); continue; }
						if (!entitiesById.TryGetValue(key.Item2, out var entity2)) { effects[i] = (null, null, null, null); continue; }

						// Compute collision response
						(var v1, var v2) = entity1.HandleCollision(entity2, entity1.World.ElasticCollisions);
						Vector2D? p1 = null, p2 = null;
						if (v1.HasValue && v2.HasValue)
						{
							(p1, p2) = entity1.CancelOverlap(entity2);
						}
						effects[i] = (p1, p2, v1, v2);
					}
				});

				// Apply effects sequentially to avoid races on shared entities
				for (int i = 0; i < keys.Count; i++)
				{
					var key = keys[i];
					if (!entitiesById.TryGetValue(key.Item1, out var entity1)) continue;
					if (!entitiesById.TryGetValue(key.Item2, out var entity2)) continue;
					var eff = effects[i];

					if (eff.p1.HasValue)
						entity1.Position = eff.p1.Value;
					if (eff.p2.HasValue)
						entity2.Position = eff.p2.Value;
					if (eff.v1.HasValue)
						entity1.v = eff.v1.Value;
					if (eff.v2.HasValue)
						entity2.v = eff.v2.Value;
				}
			}
		}

		for (int i = 0; i < entities.Length; i++)
		{
			var entity = entities[i];
			if (entity.World.ClosedBoundaries)
				entity.HandleCollisionWithWorldBoundaries();
		}
	}

	#endregion

	#region Implementation

	private static int GetTunedChunkSize(int total)
	{
		var cores = Math.Max(1, Environment.ProcessorCount);
		var min = Math.Max(1, total / (cores * 8));
		var max = Math.Max(1, total / cores);
		var preferred = Math.Max(1, total / cores);
		var tuned = Math.Max(min, Math.Min(preferred, max));
		if (tuned < 32 && total > 128) tuned = 32;
		return tuned;
	}

	[SuppressMessage("CodeQuality", "CA1859:Use concrete array type for performance", Justification = "Called from integrator with arrays; signature updated")]
	[SuppressMessage("Code Smell", "S3267:Loop should be simplified", Justification = "No LINQ in hotpath; manual loop is intentional")]
	private static async Task<Tuple<int, int>[]> ApplyPhysicsAsync(Entity[] entities)
	{
		var l = 0.0d;
		var t = 0.0d;
		var r = 0.0d;
		var b = 0.0d;

		for (int i = 0; i < entities.Length; i++)
		{
			var pos = entities[i].Position;
			l = Math.Min(l, pos.X);
			t = Math.Min(t, pos.Y);
			r = Math.Max(r, pos.X);
			b = Math.Max(b, pos.Y);
		}

		var tree = new EntityTree(new(l, t), new(r, b), 1.0d);

		for (int i = 0; i < entities.Length; i++)
			tree.Add(entities[i]);

		await Task.Run(() => tree.ComputeMassDistribution());

		var count = entities.Length;
		var chunkSize = GetTunedChunkSize(count);
		var rangeCount = (count + chunkSize - 1) / chunkSize;
		Parallel.For(0, rangeCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, rangeIndex =>
		{
			var start = rangeIndex * chunkSize;
			var end = Math.Min(start + chunkSize, count);
			for (int i = start; i < end; i++)
			{
				entities[i].a = tree.CalculateGravity(entities[i]);
			}
		});

		var collisions = tree.CollidedEntities;
		var result = new Tuple<int, int>[collisions.Count];
		for (int i = 0; i < collisions.Count; i++)
		{
			var c = collisions[i];
			result[i] = Tuple.Create(c.Item1.Id, c.Item2.Id);
		}
		return result;
	}

	#endregion
}