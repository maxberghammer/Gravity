using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class BarnesHutSimulationEngine : SimulationEngine, ISimulationEngine
	{
		#region Fields

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mPositionByEntityId = new ConcurrentDictionary<int, Vector>();

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mvByEntityId = new ConcurrentDictionary<int, Vector>();

		// ReSharper disable once InconsistentNaming
		private readonly ConcurrentDictionary<int, Vector> mgByEntityId = new ConcurrentDictionary<int, Vector>();

		#endregion

		#region Implementation of ISimulationEngine

		/// <inheritdoc />
		public async Task SimulateAsync(Entity[] aEntities, TimeSpan aDeltaTime)
		{
			var h = aDeltaTime.TotalSeconds;
			
			// k1
			var k1Entities = aEntities.Select(e => new Entity(e))
									  .ToArray();
			
			var k1Tree = await ApplyPhysicsAsync(k1Entities.Where(e => !e.IsAbsorbed)
											  .ToArray());
			var k1collidedEntityIds = k1Tree.CollidedEntities
										   .Select(t => Tuple.Create(t.Item1.Id, t.Item2.Id))
										   .ToArray();
			var a1ByEntityId = mgByEntityId.ToDictionary(p => p.Key, p => -World.G * p.Value);

			for(var i = 0; i < k1Entities.Length; i++)
			{
				var k1Entity = k1Entities[i];
				var entity = aEntities[i];

				k1Entity.Position = mPositionByEntityId.TryGetValue(k1Entity.Id, out var position)
									  ? position
									  : entity.Position + 0.5d * h * k1Entity.v;
				k1Entity.v = mvByEntityId.TryGetValue(k1Entity.Id, out var v)
							   ? v
							   : entity.v + 0.5d * h * a1ByEntityId[k1Entity.Id];
			}

			// k2
			var k2Entities = k1Entities.Select(e => new Entity(e))
									   .ToArray();
			var k2Tree = await ApplyPhysicsAsync(k2Entities.Where(e => !e.IsAbsorbed)
														   .ToArray());
			var k2collidedEntityIds = k2Tree.CollidedEntities
											.Select(t => Tuple.Create(t.Item1.Id, t.Item2.Id))
											.ToArray();
			var a2ByEntityId = mgByEntityId.ToDictionary(p => p.Key, p => -World.G * p.Value);

			for (var i = 0; i < k1Entities.Length; i++)
			{
				var k2Entity = k2Entities[i];
				var entity = aEntities[i];

				k2Entity.Position = mPositionByEntityId.TryGetValue(k2Entity.Id, out var position)
									  ? position
									  : entity.Position + 0.5d * h * k2Entity.v;
				k2Entity.v = mvByEntityId.TryGetValue(k2Entity.Id, out var v)
							   ? v
							   : entity.v + 0.5d * h * a2ByEntityId[k2Entity.Id];
			}

			// k3
			var k3Entities = k2Entities.Select(e => new Entity(e))
									   .ToArray();
			var k3Tree = await ApplyPhysicsAsync(k3Entities.Where(e => !e.IsAbsorbed)
														   .ToArray());
			var k3collidedEntityIds = k3Tree.CollidedEntities
											.Select(t => Tuple.Create(t.Item1.Id, t.Item2.Id))
											.ToArray();
			var a3ByEntityId = mgByEntityId.ToDictionary(p => p.Key, p => -World.G * p.Value);

			for (var i = 0; i < k3Entities.Length; i++)
			{
				var k3Entity = k3Entities[i];
				var entity = aEntities[i];

				k3Entity.Position = mPositionByEntityId.TryGetValue(k3Entity.Id, out var position)
										? position
										: entity.Position + h * k3Entity.v;
				k3Entity.v = mvByEntityId.TryGetValue(k3Entity.Id, out var v)
								 ? v
								 : entity.v + h * a3ByEntityId[k3Entity.Id];
			}

			// k4
			var k4Tree = await ApplyPhysicsAsync(k3Entities.Where(e => !e.IsAbsorbed)
														   .ToArray());
			var k4collidedEntityIds = k4Tree.CollidedEntities
											.Select(t => Tuple.Create(t.Item1.Id, t.Item2.Id))
											.ToArray();
			var a4ByEntityId = mgByEntityId.ToDictionary(p => p.Key, p => -World.G * p.Value);

			for (var i = 0; i < aEntities.Length; i++)
			{
				var k1Entity = k1Entities[i];
				var k2Entity = k2Entities[i];
				var k3Entity = k3Entities[i];
				var entity = aEntities[i];

				entity.Position = mPositionByEntityId.TryGetValue(entity.Id, out var position)
									  ? position
									  : entity.Position + 1.0d / 6.0d * h * (k1Entity.v + 2.0d * (k2Entity.v + k3Entity.v) + k3Entity.v);
				entity.v = mvByEntityId.TryGetValue(entity.Id, out var v)
							   ? v
							   : entity.v + 1.0d / 6.0d * h * (a1ByEntityId[entity.Id] + 2.0d * (a2ByEntityId[entity.Id] + a3ByEntityId[entity.Id]) +
															   a4ByEntityId[entity.Id]);
			}

			var entitiesById = aEntities.ToDictionary(e => e.Id);

			foreach (var collidedEntityIds in k1collidedEntityIds.Union(k2collidedEntityIds)
													 .Union(k3collidedEntityIds)
													 .Union(k4collidedEntityIds))
			{
				var entity1 = entitiesById[collidedEntityIds.Item1];
				var entity2 = entitiesById[collidedEntityIds.Item2];

				var (v1, v2) = HandleCollision(entity1, entity2, entity1.World.ElasticCollisions);

				if (v1.HasValue && v2.HasValue)
				{
					var (position1, position2) = CancelOverlap(entity1, entity2);

					if (position1.HasValue)
						entity1.Position = position1.Value;

					if (position2.HasValue)
						entity2.Position = position2.Value;
				}

				if (v1.HasValue)
					entity1.v = v1.Value;

				if (v2.HasValue)
					entity2.v = v2.Value;
			}
		}

		#endregion

		#region Implementation
		
		private async Task<EntityTree> ApplyPhysicsAsync(IReadOnlyCollection<Entity> aEntities)
		{
			var l = 0.0d;
			var t = 0.0d;
			var r = 0.0d;
			var b = 0.0d;

			foreach (var entity in aEntities)
			{
				l = Math.Min(l, entity.Position.X);
				t = Math.Min(t, entity.Position.Y);
				r = Math.Max(r, entity.Position.X);
				b = Math.Max(b, entity.Position.Y);
			}

			var tree = new EntityTree(new Vector(l, t), new Vector(r, b), 1.0d);

			foreach (var entity in aEntities)
				tree.Add(entity);

			await Task.Run(() => tree.ComputeMassDistribution());

			mPositionByEntityId.Clear();
			mvByEntityId.Clear();
			mgByEntityId.Clear();

			// Gravitation berechnen
			await Task.WhenAll(aEntities.Chunked(aEntities.Count / Environment.ProcessorCount)
										.Select(chunk => Task.Run(() =>
																  {
																	  foreach (var entity in chunk)
																		  mgByEntityId[entity.Id] = tree.CalculateGravity(entity);
																  })));

			// Kollisionen behandeln
			//foreach (var (entity1, entity2) in tree.CollidedEntities)
			//{
			//	var (v1, v2) = HandleCollision(entity1, entity2, entity1.World.ElasticCollisions);

			//	if (v1.HasValue && v2.HasValue)
			//	{
			//		var (position1, position2) = CancelOverlap(entity1, entity2);

			//		if (position1.HasValue)
			//			mPositionByEntityId[entity1.Id] = position1.Value;

			//		if (position2.HasValue)
			//			mPositionByEntityId[entity2.Id] = position2.Value;
			//	}

			//	if (v1.HasValue)
			//		mvByEntityId[entity1.Id] = v1.Value;

			//	if (v2.HasValue)
			//		mvByEntityId[entity2.Id] = v2.Value;
			//}
			return tree;
		}


		#endregion
	}
}