// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine;

[SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality", Justification = "Intentional exact check for degenerate overlap case")]
public static class EntityExtensions
{
	#region Interface

	public static (Vector2D? Position1, Vector2D? Position2) CancelOverlap(this Entity entity1, Entity entity2)
	{
		if(null==entity1)
			throw new ArgumentNullException(nameof(entity1));

		if(null == entity2)
			throw new ArgumentNullException(nameof(entity2));

		var dist = entity1.Position - entity2.Position;
		var minDistAbs = entity1.r + entity2.r;
		var r2 = dist.LengthSquared;
		if (r2 == 0.0d)
		{
			// Same position: arbitrarily separate along X
			var half = minDistAbs / 2.0d;
			return (new Vector2D(entity2.Position.X + half, entity2.Position.Y), new Vector2D(entity1.Position.X - half, entity1.Position.Y));
		}
		var invLen = 1.0d / Math.Sqrt(r2);
		var dirScaled = dist * (minDistAbs * invLen);

		if(entity1.m < entity2.m)
			return (entity2.Position + dirScaled, null);

		if(entity1.m > entity2.m)
			return (null, entity2.Position = entity1.Position - dirScaled);

		// Equal mass: move both half the overlap along normal
		var halfShift = (dist + dirScaled) / 2.0d;
		return (entity2.Position + halfShift, entity1.Position - halfShift);
	}

	public static (Vector2D? v1, Vector2D? v2) HandleCollision(this Entity entity1, Entity entity2, bool elastic)
	{
		if(null == entity1)
			throw new ArgumentNullException(nameof(entity1));

		if(null == entity2)
			throw new ArgumentNullException(nameof(entity2));

		if (entity1.IsAbsorbed || entity2.IsAbsorbed)
			return (null, null);

		var dist = entity1.Position - entity2.Position;
		var r2 = dist.LengthSquared;
		var minR = entity1.r + entity2.r;
		if (r2 >= minR * minR)
			return (null, null);

		if(elastic)
		{
			// temp = dist / (dist * dist) using cached r2
			var invR2 = 1.0d / r2;
			var temp = dist * invR2;

			var m1 = entity1.m;
			var m2 = entity2.m;
			var v1 = entity1.v;
			var v2 = entity2.v;
			var dot1 = dist * v1;
			var dot2 = dist * v2;
			var vn1 = temp * dot1;
			var vn2 = temp * dot2;
			var vt1 = v1 - vn1;
			var vt2 = v2 - vn2;
			var un1 = (m1 * vn1 + m2 * (2 * vn2 - vn1)) / (m1 + m2);
			var un2 = (m2 * vn2 + m1 * (2 * vn1 - vn2)) / (m1 + m2);

			v1 = un1 + vt1;
			v2 = un2 + vt2;

			return (v1, v2);
		}

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