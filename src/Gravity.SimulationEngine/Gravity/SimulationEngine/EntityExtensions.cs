// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;

namespace Gravity.SimulationEngine;

public static class EntityExtensions
{
	#region Interface

	/// <summary>
	///     Behandelt die Überlappung zweier gegebener Objekte und liefert, falls eine Überlappung vorliegt, gegebenenfalls die
	///     neuen Positionen der beiden Objekte, so dass sie sich nicht mehr überlappen.
	/// </summary>
	public static (Vector2D? Position1, Vector2D? Position2) CancelOverlap(this Entity entity1, Entity entity2)
	{
		if(null==entity1)
			throw new ArgumentNullException(nameof(entity1));

		if(null == entity2)
			throw new ArgumentNullException(nameof(entity2));

		var dist = entity1.Position - entity2.Position;
		var minDistAbs = entity1.r + entity2.r;

		if(entity1.m < entity2.m)
			return (entity2.Position + dist.Unit() * minDistAbs, null);

		if(entity1.m > entity2.m)
			return (null, entity2.Position = entity1.Position - dist.Unit() * minDistAbs);

		return (entity2.Position + (dist + dist.Unit() * minDistAbs) / 2, entity1.Position - (dist + dist.Unit() * minDistAbs) / 2);
	}

	/// <summary>
	///     Behandelt die Kollision zweier gegebener Objekte und liefert, falls eine Kollision vorliegt, gegebenenfalls die
	///     neuen Geschwindigkeiten der beiden Objekte.
	/// </summary>
	public static (Vector2D? v1, Vector2D? v2) HandleCollision(this Entity entity1, Entity entity2, bool elastic)
	{
		if(null == entity1)
			throw new ArgumentNullException(nameof(entity1));

		if(null == entity2)
			throw new ArgumentNullException(nameof(entity2));

		if (entity1.IsAbsorbed ||
		   entity2.IsAbsorbed)
			return (null, null);

		var dist = entity1.Position - entity2.Position;

		if(dist.Length >= entity1.r + entity2.r)
			return (null, null);

		if(elastic)
		{
			var temp = dist / (dist * dist);

			// Masse Objekt 1
			var m1 = entity1.m;
			// Masse Objekt 2
			var m2 = entity2.m;
			// Geschwindigkeitsvektor Objekt 1
			var v1 = entity1.v;
			// Geschwindigkeitsvektor Objekt 2
			var v2 = entity2.v;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1
			var vn1 = temp * (dist * v1);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2
			var vn2 = temp * (dist * v2);
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 1
			var vt1 = v1 - vn1;
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 2
			var vt2 = v2 - vn2;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1 so korrigieren, dass die Objekt-Massen mit einfließen
			var un1 = (m1 * vn1 + m2 * (2 * vn2 - vn1)) / (m1 + m2);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2 so korrigieren, dass die Objekt-Massen mit einfließen
			var un2 = (m2 * vn2 + m1 * (2 * vn1 - vn2)) / (m1 + m2);

			v1 = un1 + vt1;
			v2 = un2 + vt2;

			//Debug.Assert(aEntity1.p.Length+aEntity2.p.Length==(v1*aEntity1.m).Length+(v2*aEntity2.m).Length);
			//Debug.Assert(aEntity1.Ekin+aEntity2.Ekin== 0.5d*(aEntity1.m * v1.LengthSquared + aEntity2.m * v2.LengthSquared));

			return (v1, v2);
		}

		// Vereinigung behandeln
		var v = (entity1.p + entity2.p) / (entity1.m + entity2.m);

		if(entity2.m > entity1.m)
		{
			entity2.Absorb(entity1);

			return (null, v);
		}

		entity1.Absorb(entity2);

		return (v, null);
	}

	public static void HandleCollisionWithWorldBoundaries(this Entity entity)
	{
		if(null == entity)
			throw new ArgumentNullException(nameof(entity));
		
		var topLeft = entity.World.Viewport.TopLeft + new Vector2D(entity.r, entity.r);
		var bottomRight = entity.World.Viewport.BottomRight - new Vector2D(entity.r, entity.r);

		if(entity.Position.X < topLeft.X)
		{
			entity.v = new(-entity.v.X, entity.v.Y);
			entity.Position = new(topLeft.X, entity.Position.Y);
		}

		if(entity.Position.X > bottomRight.X)
		{
			entity.v = new(-entity.v.X, entity.v.Y);
			entity.Position = new(bottomRight.X, entity.Position.Y);
		}

		if(entity.Position.Y < topLeft.Y)
		{
			entity.v = new(entity.v.X, -entity.v.Y);
			entity.Position = new(entity.Position.X, topLeft.Y);
		}

		if(entity.Position.Y > bottomRight.Y)
		{
			entity.v = new(entity.v.X, -entity.v.Y);
			entity.Position = new(entity.Position.X, bottomRight.Y);
		}
	}

	#endregion
}