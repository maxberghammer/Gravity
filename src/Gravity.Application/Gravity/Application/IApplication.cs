using Gravity.SimulationEngine;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Wellenlib.Diagnostics;

namespace Gravity.Application.Gravity.Application;

public interface IApplication
{
	#region Internal types

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

	public sealed class EngineType
	{
		#region Interface

		public Factory.SimulationEngineType Type { get; init; }

		public required string Name { get; init; }

		#endregion
	}

	public interface IViewport
	{
		/// <summary>
		/// Camera yaw angle in radians (rotation around Y axis)
		/// </summary>
		double CameraYaw { get; }

		/// <summary>
		/// Camera pitch angle in radians (rotation around X axis, clamped to avoid gimbal lock)
		/// </summary>
		double CameraPitch { get; }

		/// <summary>
		/// Camera distance from center
		/// </summary>
		double CameraDistance { get; }

		Vector3D Center { get; }

		Vector2 ToViewport(Vector3D worldPoint);

		float ToViewport(double worldLength);

		Vector3D ToWorld(Vector2 viewportPoint);

		double ToWorld(float viewportLength);

		void Scale(double scale);

		/// <summary>
		/// Rotates the camera by the given delta angles. When snap is true, the rotation snaps to 45° increments.
		/// </summary>
		void RotateCamera(double deltaYaw, double deltaPitch, bool snap = false);

		void Resize(Vector3D newSize);

		void Zoom(Vector3D zoomCenter, double zoomFactor);

		void EnableAutocenter();

		void DisableAutocenter();

		void AutoScaleAndCenter();
	}

	public interface IWorld
	{
		int BodyCount { get; }

		TimeSpan ToWorld(TimeSpan applicationTimeSpan);

		TimeSpan ToApplication(TimeSpan worldTimeSpan);

		void SetTimescale(double timeScale);

		void EnableElasticCollisions();

		void DisableElasticCollisions();

		void EnableClosedBoundaries();

		void DisableClosedBoundaries();

		IReadOnlyList<Body> GetBodies();
	}

	#endregion

	IViewport Viewport { get; }

	IWorld World { get; }

	IReadOnlyList<BodyPreset> BodyPresets { get; }

	IReadOnlyList<EngineType> EngineTypes { get; }

	FrameDiagnostics FrameDiagnostics { get; }

	TimeSpan Runtime { get; }

	void Select(BodyPreset bodyPreset);

	void Select(EngineType engineType);

	void Select(Body? body);

	void SetSimulationFrequency(double frequencyInHz);

	Body? FindClosestBody(Vector2 viewportPoint, double viewportSearchRadius);

	void AddBody(Vector3D position, Vector3D velocity);

	void AddOrbitBody(Vector3D position, Vector3D velocity);

	void AddRandomBodies(int count, bool enableRespawn, bool stableOrbits);

	void StopRespawn();

	void StartSimulation();

	void StopSimulation();
	
	void Reset();

	Task SaveAsync(string filePath);

	Task OpenAsync(string filePath);
}