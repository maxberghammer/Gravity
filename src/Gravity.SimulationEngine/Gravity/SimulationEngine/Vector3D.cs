using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Gravity.SimulationEngine;

[SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Nö, das ist halt Mathe")]
public readonly struct Vector3D : IEquatable<Vector3D>
{
	#region Fields

	public static readonly Vector3D Zero = new(0, 0, 0);

	#endregion

	#region Construction

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3D(double x, double y, double z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	#endregion

	#region Interface

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator*(Vector3D a, double b)
		=> new(a.X * b, a.Y * b, a.Z * b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator*(double a, Vector3D b)
		=> b * a;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator/(Vector3D a, double b)
		=> new(a.X / b, a.Y / b, a.Z / b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator+(Vector3D a, Vector3D b)
		=> new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator-(Vector3D a, Vector3D b)
		=> new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3D operator-(Vector3D a)
		=> new(-a.X, -a.Y, -a.Z);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double operator*(Vector3D a, Vector3D b)
		=> a.X * b.X + a.Y * b.Y + a.Z * b.Z;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator==(Vector3D left, Vector3D right)
		=> left.Equals(right);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator!=(Vector3D left, Vector3D right)
		=> !left.Equals(right);

	public double X { get; }

	public double Y { get; }

	public double Z { get; }

	public double LengthSquared
		=> X * X + Y * Y + Z * Z;

	public double Length
		=> Math.Sqrt(LengthSquared);

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is Vector3D other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> HashCode.Combine(X, Y, Z);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3D Unit()
		=> this / Length;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public (Vector3D unit, double length) UnitWithLength()
	{
		var len = Length;
		return (this / len, len);
	}
	
	/// <summary>
	/// Returns the cross product of two 3D vectors.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3D Cross(Vector3D other)
		=> new(Y * other.Z - Z * other.Y,
			   Z * other.X - X * other.Z,
			   X * other.Y - Y * other.X);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3D Round(int aDecimals)
		=> new(Math.Round(X, aDecimals), Math.Round(Y, aDecimals), Math.Round(Z, aDecimals));

	#endregion

	#region Implementation of IEquatable<Vector3D>

	/// <inheritdoc/>
	bool IEquatable<Vector3D>.Equals(Vector3D other)
		=> Equals(other);

	#endregion

	#region Implementation

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Equals(Vector3D other)
		=> X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

	#endregion
}
