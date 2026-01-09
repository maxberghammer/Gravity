using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Gravity.SimulationEngine;

[SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "Nö, das ist halt Mathe")]
public readonly struct Vector2D : IEquatable<Vector2D>
{
	#region Fields

	public static readonly Vector2D Zero = new(0, 0);

	#endregion

	#region Construction

	public Vector2D(double x, double y)
	{
		X = x;
		Y = y;
	}

	#endregion

	#region Interface

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator*(Vector2D a, double b)
		=> new(a.X * b, a.Y * b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator*(double a, Vector2D b)
		=> b * a;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator/(Vector2D a, double b)
		=> new(a.X / b, a.Y / b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator+(Vector2D a, Vector2D b)
		=> new(a.X + b.X, a.Y + b.Y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator-(Vector2D a, Vector2D b)
		=> new(a.X - b.X, a.Y - b.Y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2D operator-(Vector2D a)
		=> new(-a.X, -a.Y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double operator*(Vector2D a, Vector2D b)
		=> a.X * b.X + a.Y * b.Y;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator==(Vector2D left, Vector2D right)
		=> left.Equals(right);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator!=(Vector2D left, Vector2D right)
		=> !left.Equals(right);

	public double X { get; }

	public double Y { get; }

	public double LengthSquared
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => X * X + Y * Y;
	}

	public double Length
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Math.Sqrt(LengthSquared);
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is Vector2D other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> HashCode.Combine(X, Y);

	public Vector2D Unit()
		=> this / Length;

	public Vector2D Norm()
		=> new(Y, -X);

	public Vector2D Round(int aDecimals)
		=> new(Math.Round(X, aDecimals), Math.Round(Y, aDecimals));

	#endregion

	#region Implementation of IEquatable<Vector2D>

	/// <inheritdoc/>
	bool IEquatable<Vector2D>.Equals(Vector2D other)
		=> Equals(other);

	#endregion

	#region Implementation

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool Equals(Vector2D other)
		=> X.Equals(other.X) && Y.Equals(other.Y);

	#endregion
}