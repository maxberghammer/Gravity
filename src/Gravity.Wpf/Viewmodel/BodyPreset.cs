// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using Gravity.SimulationEngine;

namespace Gravity.Wpf.Viewmodel;

public class BodyPreset
{
	#region Construction

	public BodyPreset(string name, double mass, double radius, Color color, Guid id)
		: this(name, mass, radius, color, null, 0, id)
	{
	}

	public BodyPreset(string name, double mass, double radius, Color color, Color? atmosphereColor, double atmosphereThickness, Guid id)
	{
		Name = name;
		m = mass;
		r = radius;
		Color = color;
		AtmosphereColor = atmosphereColor;
		AtmosphereThickness = atmosphereThickness;
		Id = id;
	}

	#endregion

	#region Interface

	public static BodyPreset FromDensity(string name,
										 double density,
										 double radius,
										 Color color,
										 Color atmosphereColor,
										 double atmosphereThickness,
										 Guid id)
		=> new(name, 4.0d / 3.0d * Math.Pow(radius, 3) * Math.PI * density, radius, color, atmosphereColor, atmosphereThickness, id);

	public string Name { get; }

	// ReSharper disable once InconsistentNaming
	public double m { get; }

	// ReSharper disable once InconsistentNaming
	public double r { get; }

	public Color Color { get; }

	public Color? AtmosphereColor { get; }

	public double AtmosphereThickness { get; }

	public Guid Id { get; }

	#endregion
}