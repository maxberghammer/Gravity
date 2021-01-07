using System;
using System.Diagnostics;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class SimulationEngine
	{
		#region Implementation

		/// <summary>
		///     Behandelt die Überlappung zweier gegebener Objekte und liefert, falls eine Überlappung vorliegt, gegebenenfalls die
		///     neuen Positionen der beiden Objekte, so dass sie sich nicht mehr überlappen.
		/// </summary>
		protected static (Vector? Position1, Vector? Position2) CancelOverlap(Entity aEntity1, Entity aEntity2)
		{
			var dist = aEntity1.Position - aEntity2.Position;
			var minDistAbs = (aEntity1.r + aEntity2.r);

			if (aEntity1.m < aEntity2.m)
				return (aEntity2.Position + dist.Unit() * minDistAbs, null);

			if (aEntity1.m > aEntity2.m)
				return (null, aEntity2.Position = aEntity1.Position - dist.Unit() * minDistAbs);

			return (aEntity2.Position + (dist + dist.Unit() * minDistAbs) / 2, aEntity1.Position - (dist + dist.Unit() * minDistAbs) / 2);
		}

		/// <summary>
		///     Behandelt die Kollision zweier gegebener Objekte und liefert, falls eine Kollision vorliegt, gegebenenfalls die
		///     neuen Geschwindigkeiten der beiden Objekte.
		/// </summary>
		protected static (Vector? v1, Vector? v2) HandleCollision(Entity aEntity1, Entity aEntity2, bool aElastic)
		{
			if (aEntity1.IsAbsorbed || aEntity2.IsAbsorbed)
				return (null, null);

			var dist = aEntity1.Position - aEntity2.Position;

			if (dist.Length >= (aEntity1.r + aEntity2.r))
				return (null, null);

			if (aElastic)
			{
				var temp = dist / (dist * dist);

				// Masse Objekt 1
				var m1 = aEntity1.m;
				// Masse Objekt 2
				var m2 = aEntity2.m;
				// Geschwindigkeitsvektor Objekt 1
				var v1 = aEntity1.v;
				// Geschwindigkeitsvektor Objekt 2
				var v2 = aEntity2.v;
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
			var v = (aEntity1.p + aEntity2.p) / (aEntity1.m + aEntity2.m);

			if (aEntity2.m > aEntity1.m)
			{
				aEntity2.Absorb(aEntity1);
				return (null, v);
			}

			aEntity1.Absorb(aEntity2);
			return (v, null);
		}

		protected static void HandleCollisionWithWorldBoundaries(Entity aEntity)
		{
			var topLeft = aEntity.World.Viewport.TopLeft + new Vector(aEntity.r, aEntity.r);
			var bottomRight = aEntity.World.Viewport.BottomRight - new Vector(aEntity.r, aEntity.r);

			if (aEntity.Position.X < topLeft.X)
			{
				aEntity.v = new Vector(-aEntity.v.X, aEntity.v.Y);
				aEntity.Position = new Vector(topLeft.X, aEntity.Position.Y);
			}

			if (aEntity.Position.X > bottomRight.X)
			{
				aEntity.v = new Vector(-aEntity.v.X, aEntity.v.Y);
				aEntity.Position = new Vector(bottomRight.X, aEntity.Position.Y);
			}

			if (aEntity.Position.Y < topLeft.Y)
			{
				aEntity.v = new Vector(aEntity.v.X, -aEntity.v.Y);
				aEntity.Position = new Vector(aEntity.Position.X, topLeft.Y);
			}

			if (aEntity.Position.Y > bottomRight.Y)
			{
				aEntity.v = new Vector(aEntity.v.X, -aEntity.v.Y);
				aEntity.Position = new Vector(aEntity.Position.X, bottomRight.Y);
			}
		}

		#endregion
	}
}