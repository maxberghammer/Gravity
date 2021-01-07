﻿using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Gravity.Viewmodel
{
	internal class Entity : NotifyPropertyChanged
	{
		#region Fields

		private static int mMaxId;

		// ReSharper disable once InconsistentNaming
		private Vector mv;

		// ReSharper disable once InconsistentNaming
		private double mm;

		// ReSharper disable once InconsistentNaming
		private Vector ma;

		#endregion

		#region Construction

		// ReSharper disable InconsistentNaming
		public Entity(Vector aPosition, double ar, double am, Vector av, Vector aa, World aWorld, SolidColorBrush aFill, SolidColorBrush aStroke, double aStrokeWidth)
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
			a = aa;
			Id = mMaxId++;
		}

		public Entity(Entity aOther)
		{
			World = aOther.World;
			Position = aOther.Position;
			v = aOther.v;
			r = aOther.r;
			m = aOther.m;
			a = aOther.a;
			Id = aOther.Id;
		}

		#endregion

		#region Interface

		public int Id { get; }

		public bool IsAbsorbed { get; private set; }

		public SolidColorBrush Fill { get; }

		public SolidColorBrush Stroke { get; }

		public double StrokeWidth { get; }

		public World World { get; }

		public Vector Position { get; set; }

		// ReSharper disable once InconsistentNaming
		public Vector v
		{
			get => mv;
			set => SetProperty(ref mv, (value.Length > 300000000)
										   ? value.Unit() * 300000000
										   : value);
		}

		// ReSharper disable once InconsistentNaming
		public Vector a { get => ma; set => SetProperty(ref ma, value); }

		// ReSharper disable once InconsistentNaming
		public double r { get; private set; }

		// ReSharper disable once InconsistentNaming
		public double m { get => mm; private set => SetProperty(ref mm, value); }

		// ReSharper disable once InconsistentNaming
		public Vector p
			=> m * v;

		public double Ekin
			=> 0.5d * m * v.LengthSquared;

		public void Absorb(Entity aOther)
		{
			m += aOther.m;
			r = Math.Pow(Math.Pow(r, 3) + Math.Pow(aOther.r, 3), 1.0d / 3.0d);
			aOther.IsAbsorbed = true;
		}

		#endregion
	}
}