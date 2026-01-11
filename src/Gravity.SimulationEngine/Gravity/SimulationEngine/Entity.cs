// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine;

public class Entity
{
	#region Fields

	private static int _maxId;

	#endregion

	#region Construction

	// ReSharper disable InconsistentNaming
	[SuppressMessage("Major Code Smell", "S3010:Static fields should not be updated in constructors", Justification = "<Pending>")]
	public Entity(Vector2D position,
				  double radius,
				  double mass,
				  Vector2D velocity,
				  Vector2D acceleration,
				  IWorld world,
				  Color fill,
				  Color? stroke,
				  double strokeWidth)
		// ReSharper restore InconsistentNaming
	{
		World = world;
		Position = position;
		v = velocity;
		Fill = fill;
		Stroke = stroke;
		StrokeWidth = strokeWidth;
		r = radius;
		r2 = radius * radius;
		m = mass;
		a = acceleration;
		Id = _maxId++;
	}

	public Entity(Entity other)
	{
		if(null == other)
			throw new ArgumentNullException(nameof(other));

		World = other.World;
		Position = other.Position;
		v = other.v;
		r = other.r;
		r2 = other.r2;
		m = other.m;
		a = other.a;
		Id = other.Id;
	}

	#endregion

	#region Interface

	public int Id { get; }

	public bool IsAbsorbed { get; private set; }

	public Color Fill { get; }

	public Color? Stroke { get; }

	public double StrokeWidth { get; }

	public IWorld World { get; }

	public Vector2D Position { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector2D v { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector2D a { get; set; }

	// ReSharper disable once InconsistentNaming
	public double r { get; private set; }

	// Cached squared radius for faster collision checks
	public double r2 { get; private set; }

	// ReSharper disable once InconsistentNaming
	public double m { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector2D p
		=> m * v;

	// ReSharper disable once UnusedMember.Global
	public double Ekin
		=> 0.5d * m * v.LengthSquared;

	public void Absorb(Entity other)
	{
		if(null == other)
			throw new ArgumentNullException(nameof(other));

		m += other.m;
		r = Math.Pow(Math.Pow(r, 3) + Math.Pow(other.r, 3), 1.0d / 3.0d);
		r2 = r * r;
		other.IsAbsorbed = true;
	}

	#endregion
}