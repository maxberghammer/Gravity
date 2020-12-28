using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Gravity.Viewmodel
{
	internal class Entity : NotifyPropertyChanged
	{
		#region Fields

		private static int mMaxId;

		// ReSharper disable once InconsistentNaming
		private Vector mg = VectorExtensions.Zero;
		
		// ReSharper disable once InconsistentNaming
		private Vector? mvToSet;

		// ReSharper disable once InconsistentNaming
		private Vector mv;

		// ReSharper disable once InconsistentNaming
		private double mm;
		
		#endregion

		#region Construction

		// ReSharper disable InconsistentNaming
		public Entity(Vector aPosition, double ar, double am, Vector av, World aWorld, SolidColorBrush aFill, SolidColorBrush aStroke, double aStrokeWidth)
			// ReSharper restore InconsistentNaming
		{
			World = aWorld;
			Position = aPosition;
			v = av;
			Fill = aFill;
			Stroke = aStroke;
			StrokeWidth = aStrokeWidth;
			r = ar;
			m = am;
			Id = mMaxId++;
		}

		#endregion

		#region Interface

		public int Id { get; }

		public bool IsAbsorbed { get; private set; }

		public SolidColorBrush Fill { get; }

		public SolidColorBrush Stroke { get; }

		public double StrokeWidth { get; }

		public World World { get; }

		public Vector Position { get; private set; }

		// ReSharper disable once InconsistentNaming
		public Vector v
		{
			get => mv;
			set => SetProperty(ref mv, (value.Length > 300000000)
										   ? value.Unit() * 300000000
										   : value);
		}

		// ReSharper disable once InconsistentNaming
		public double r { get; set; }

		// ReSharper disable once InconsistentNaming
		public double m { get => mm; private set => SetProperty(ref mm, value); }

		// ReSharper disable once InconsistentNaming
		public Vector p
			=> m * v;

		public double Ekin
			=> 0.5d * m * v.LengthSquared;

		public void UpdatePosition(TimeSpan aDeltaTime)
		{
			if (IsAbsorbed)
				return;

			if (null != mvToSet)
				v = mvToSet.Value;

			mvToSet = null;

			v -= World.G * mg * aDeltaTime.TotalSeconds;

			mg = VectorExtensions.Zero;

			Position += v * aDeltaTime.TotalSeconds;

			if (World.ClosedBoundaries)
				HandleCollisionWithWorldBoundaries();
		}
		
		public void ApplyPhysics(IEnumerable<Entity> aOthers)
		{
			if (IsAbsorbed)
				return;

			foreach (var other in aOthers.Where(e => !e.IsAbsorbed))
			{
				// Kollision behandeln
				HandleCollision(other, World.ElasticCollisions);

				if (IsAbsorbed)
					return;
				
				var dist = Position - other.Position;

				// Gravitationsbeschleunigung integrieren
				var temp = dist / Math.Pow(dist.LengthSquared, 1.5d);

				lock(this)
					mg += other.m * temp;

				lock(other)
					other.mg -= m * temp;
			}
		}

		#endregion

		#region Implementation

		private void HandleCollision(Entity aOther, bool aElastic)
		{
			var dist = Position - aOther.Position;

			if (dist.Length >= (r + aOther.r))
				return;

			if (aElastic)
			{
				// Position korrigieren um Überlappungen zu vermeiden
				var corr = ((r + aOther.r) - dist.Length) * dist.Unit();

				if (m < aOther.m)
				{
					Position += corr;
				}
				else if (m > aOther.m)
				{
					aOther.Position -= corr;
				}
				else
				{
					Position += corr / 2;
					aOther.Position -= corr / 2;
				}

				var temp = dist / (dist * dist);

				// Masse Objekt 1
				var m1 = m;
				// Masse Objekt 2
				var m2 = aOther.m;
				// Geschwindigkeitsvektor Objekt 1
				var v1 = v;
				// Geschwindigkeitsvektor Objekt 2
				var v2 = aOther.v;
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

				mvToSet = un1 + vt1;
				aOther.mvToSet = un2 + vt2;

				//Debug.Assert(p+aOther.p==(mvToSet*m)+(aOther.mvToSet*aOther.m));
				//Debug.Assert(Ekin+aOther.Ekin== 0.5d*(m * mvToSet.Value.LengthSquared + aOther.m * aOther.mvToSet.Value.LengthSquared));
				
				return;
			}

			// Vereinigung behandeln
			if (aOther.m > m)
			{
				aOther.mvToSet = (p + aOther.p) / (m + aOther.m);
				aOther.m += m;
				aOther.r = Math.Pow(Math.Pow(r, 3) + Math.Pow(aOther.r, 3), 1.0d / 3.0d);
				IsAbsorbed = true;

				return;
			}

			mvToSet = (p + aOther.p) / (m + aOther.m);
			m += aOther.m;
			r = Math.Pow(Math.Pow(r, 3) + Math.Pow(aOther.r, 3), 1.0d / 3.0d);
			aOther.IsAbsorbed = true;
		}

		private void HandleCollisionWithWorldBoundaries()
		{
			var topLeft = World.Viewport.TopLeft + new Vector(r, r);
			var bottomRight = World.Viewport.BottomRight - new Vector(r, r);

			if (Position.X < topLeft.X)
			{
				v = new Vector(-v.X, v.Y);
				Position = new Vector(topLeft.X, Position.Y);
			}

			if (Position.X > bottomRight.X)
			{
				v = new Vector(-v.X, v.Y);
				Position = new Vector(bottomRight.X, Position.Y);
			}

			if (Position.Y < topLeft.Y)
			{
				v = new Vector(v.X, -v.Y);
				Position = new Vector(Position.X, topLeft.Y);
			}

			if (Position.Y > bottomRight.Y)
			{
				v = new Vector(v.X, -v.Y);
				Position = new Vector(Position.X, bottomRight.Y);
			}
		}

		#endregion
	}
}