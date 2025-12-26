using System;
using System.Globalization;

namespace Gravity.SimulationEngine;

public struct Color : IEquatable<Color>
{
	#region Fields

	public static readonly Color Black = new(0xFF000000);
	public static readonly Color Blue = new(0xFF0000FF);
	public static readonly Color DarkGray = new(0xFFA9A9A9);
	public static readonly Color Green = new(0xFF008000);
	public static readonly Color Red = new(0xFFFF0000);
	public static readonly Color White = new(0xFFFFFFFF);
	public static readonly Color Yellow = new(0xFFFFFF00);

	#endregion

	#region Construction

	public Color(uint argb)
	{
		A = (byte)((argb >> 24) & 0xFF);
		R = (byte)((argb >> 16) & 0xFF);
		G = (byte)((argb >> 8) & 0xFF);
		B = (byte)(argb & 0xFF);
	}

	public Color(byte a, byte r, byte g, byte b)
	{
		A = a;
		R = r;
		G = g;
		B = b;
	}

	#endregion

	#region Interface

	public static bool operator==(Color left, Color right)
		=> left.Equals(right);

	public static bool operator!=(Color left, Color right)
		=> !left.Equals(right);

	public static Color Parse(string color)
		=> uint.TryParse(color, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var c)
			   ? new Color(c)
			   : throw new FormatException($"Invalid color format: {color}");

	public byte A { get; set; }

	public byte R { get; set; }

	public byte G { get; set; }

	public byte B { get; set; }

	public float ScA
		=> A / 255.0f;

	public float ScR
		=> R / 255.0f;

	public float ScG
		=> G / 255.0f;

	public float ScB
		=> B / 255.0f;

	/// <inheritdoc/>
	public override string ToString()
		=> $"#{A:X2}{R:X2}{G:X2}{B:X2}";

	/// <inheritdoc/>
	public override bool Equals(object obj)
		=> obj is Color other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> HashCode.Combine(A, R, G, B);

	#endregion

	#region Implementation of IEquatable<Color>

	/// <inheritdoc/>
	bool IEquatable<Color>.Equals(Color other)
		=> A == other.A && R == other.R && G == other.G && B == other.B;

	#endregion
}