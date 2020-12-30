using System;
using System.Windows;
using Gravity.Viewmodel;

namespace Gravity.SimulationEngine
{
	internal class SimulationEngine
	{
		#region Implementation

		// ReSharper disable InconsistentNaming
		protected static void Update(Entity aEntity, Vector? av, Vector? ag, TimeSpan aDeltaTime)
			// ReSharper restore InconsistentNaming
		{
			if (aEntity.IsAbsorbed)
				return;

			// Bei Bedarf neue Geschwindigkeit übernehmen
			if (av.HasValue)
				aEntity.v = av.Value;

			// Bei Bedarf Gravitationsbeschleunigung anwenden
			if (ag.HasValue)
				aEntity.v -= World.G * ag.Value * aDeltaTime.TotalSeconds;

			// Position aktualisieren
			aEntity.Position += aEntity.v * aDeltaTime.TotalSeconds;

			if (aEntity.World.ClosedBoundaries)
				HandleCollisionWithWorldBoundaries(aEntity);
		}

		private static void HandleCollisionWithWorldBoundaries(Entity aEntity)
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

		/// <summary>
		/// Behandelt die Kollision zweier gegebener Objekte und liefert, falls eine Kollision vorliegt, gegebenenfalls die neuen Geschwindigkeiten der beiden Objekte.
		/// </summary>
		protected static (Vector? v1, Vector? v2) HandleCollision(Entity aEntity1, Entity aEntity2, bool aElastic)
		{
			var dist = aEntity1.Position - aEntity2.Position;

			if (dist.Length >= (aEntity1.r + aEntity2.r))
				return (null, null);

			if (aElastic)
			{
				// Position korrigieren um Überlappungen zu vermeiden
				var corr = ((aEntity1.r + aEntity2.r) - dist.Length) * dist.Unit();

				if (aEntity1.m < aEntity2.m)
				{
					aEntity1.Position += corr;
				}
				else if (aEntity1.m > aEntity2.m)
				{
					aEntity2.Position -= corr;
				}
				else
				{
					aEntity1.Position += corr / 2;
					aEntity2.Position -= corr / 2;
				}

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

				//Debug.Assert(p+aOther.p==(mvToSet*m)+(aOther.mvToSet*aOther.m));
				//Debug.Assert(Ekin+aOther.Ekin== 0.5d*(m * mvToSet.Value.LengthSquared + aOther.m * aOther.mvToSet.Value.LengthSquared));

				return (un1 + vt1, un2 + vt2);
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

		#endregion
	}
}