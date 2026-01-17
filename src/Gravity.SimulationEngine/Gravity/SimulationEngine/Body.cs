// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Diagnostics.CodeAnalysis;

namespace Gravity.SimulationEngine;

public class Body
{
	#region Fields

	private static int _maxId;

	#endregion

	#region Construction

	// ReSharper disable InconsistentNaming
	[SuppressMessage("Major Code Smell", "S3010:Static fields should not be updated in constructors", Justification = "<Pending>")]
	public Body(Vector3D position,
				  double radius,
				  double mass,
				  Vector3D velocity,
				  Vector3D acceleration,
				  Color fill,
				  Color? stroke,
				  double strokeWidth)
		// ReSharper restore InconsistentNaming
	{
		Position = position;
		v = velocity;
		Color = fill;
		AtmosphereColor = stroke;
		AtmosphereThickness = strokeWidth;
		r = radius;
		r2 = radius * radius;
		m = mass;
		a = acceleration;
		Id = _maxId++;
	}

	public Body(Body other)
	{
        ArgumentNullException.ThrowIfNull(other);

		Position = other.Position;
		v = other.v;
		r = other.r;
		r2 = other.r2;
		m = other.m;
		a = other.a;
		Id = other.Id;
	}

	public Body Clone(bool cloneAccelleration = false, bool cloneId = false)
		=> cloneId
			   ? new(this)
			   : new(new(Position.X, Position.Y, Position.Z),
					 r,
					 m,
					 new(v.X, v.Y, v.Z),
					 cloneAccelleration
						 ? a
						 : Vector3D.Zero,
					 Color,
					 AtmosphereColor,
					 AtmosphereThickness);

	#endregion

	#region Interface

	public int Id { get; }

	public bool IsAbsorbed { get; private set; }

	public Color Color { get; }

	public Color? AtmosphereColor { get; }

	public double AtmosphereThickness { get; }
	
	public Vector3D Position { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector3D v { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector3D a { get; set; }

	// ReSharper disable once InconsistentNaming
	public double r { get; private set; }

	// Cached squared radius for faster collision checks
	// ReSharper disable once InconsistentNaming
	public double r2 { get; private set; }

	// ReSharper disable once InconsistentNaming
	public double m { get; set; }

	// ReSharper disable once InconsistentNaming
	public Vector3D p
		=> m * v;

	// ReSharper disable once UnusedMember.Global
	public double Ekin
		=> 0.5d * m * v.LengthSquared;

	public void Absorb(Body other)
	{
        ArgumentNullException.ThrowIfNull(other);

        m += other.m;
		r = Math.Pow(Math.Pow(r, 3) + Math.Pow(other.r, 3), 1.0d / 3.0d);
		r2 = r * r;
		other.IsAbsorbed = true;
	}

	#endregion
}