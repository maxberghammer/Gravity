// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using Gravity.SimulationEngine;

namespace Gravity.Wpf.Viewmodel;

public class BodyPreset
{
	#region Construction

	public BodyPreset(string name, double mass, double radius, Color fill, Guid id)
		: this(name, mass, radius, fill, null, 0, id)
	{
	}

	public BodyPreset(string name, double mass, double radius, Color fill, Color? stroke, double strokeWidth, Guid id)
	{
		Name = name;
		m = mass;
		r = radius;
		Fill = fill;
		Stroke = stroke;
		StrokeWidth = strokeWidth;
		Id = id;
	}

	#endregion

	#region Interface

	public static BodyPreset FromDensity(string name,
										 double density,
										 double radius,
										 Color fill,
										 Color stroke,
										 double strokeWidth,
										 Guid id)
		=> new(name, 4.0d / 3.0d * Math.Pow(radius, 3) * Math.PI * density, radius, fill, stroke, strokeWidth, id);

	public string Name { get; }

	// ReSharper disable once InconsistentNaming
	public double m { get; }

	// ReSharper disable once InconsistentNaming
	public double r { get; }

	public Color Fill { get; }

	public Color? Stroke { get; }

	public double StrokeWidth { get; }

	public Guid Id { get; }

	#endregion
}