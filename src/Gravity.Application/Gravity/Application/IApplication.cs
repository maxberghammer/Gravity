using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;
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
		double CurrentCameraYaw { get; }

		/// <summary>
		/// Camera pitch angle in radians (rotation around X axis)
		/// </summary>
		double CurrentCameraPitch { get; }

		/// <summary>
		/// Camera distance from center
		/// </summary>
		double CurrentCameraDistance { get; }

		/// <summary>
		/// Gets the center point of the viewport in three-dimensional space.
		/// </summary>
		Vector3D CurrentCenter { get; }

		/// <summary>
		/// Gets the current scale factor of the viewport, defined as the ratio of viewport size to world size.
		/// </summary>
		double CurrentScale { get; }

		/// <summary>
		/// Converts a point from world coordinates to viewport coordinates.
		/// </summary>
		/// <param name="worldPoint">The point in world space to be transformed to viewport space.</param>
		/// <returns>A <see cref="Vector2"/> representing the position of the specified world point in viewport coordinates.</returns>
		Vector2 ToViewport(Vector3D worldPoint);

		/// <summary>
		/// Converts a length from world coordinates to viewport coordinates.
		/// </summary>
		/// <param name="worldLength">The length in world units to convert. Must be a finite value.</param>
		/// <returns>A single-precision floating-point value representing the equivalent length in viewport coordinates.</returns>
		float ToViewport(double worldLength);

		/// <summary>
		/// Converts a point from viewport coordinates to world coordinates.
		/// </summary>
		/// <param name="viewportPoint">The point in viewport space to convert.</param>
		/// <returns>A <see cref="Vector3D"/> representing the corresponding position in world space.</returns>
		Vector3D ToWorld(Vector2 viewportPoint);

		/// <summary>
		/// Converts a length from viewport coordinates to world coordinates.
		/// </summary>
		/// <param name="viewportLength">The length in viewport units to convert.</param>
		/// <returns>The equivalent length in world coordinates as a double-precision floating-point value.</returns>
		double ToWorld(float viewportLength);

		/// <summary>
		/// Sets the scale of the viewport to the specified factor.
		/// </summary>
		/// <param name="scale">The new scale-factor representing Viewport-Size/World-Size.</param>
		void Scale(double scale);

		/// <summary>
		/// Rotates the camera by the given delta angles. When snap is true, the rotation snaps to 45° increments.
		/// </summary>
		void RotateCamera(double deltaYaw, double deltaPitch, bool snap = false);

		/// <summary>
		/// Resizes the viewport to the specified dimensions.
		/// </summary>
		/// <param name="newSize">
		/// The new size of the viewport, represented as a <see cref="Vector3D"/> specifying width, height, and depth. Each
		/// component must be non-negative.
		/// </param>
		void Resize(Vector3D newSize);

		/// <summary>
		/// Zooms the view by the specified factor, centering the zoom operation at the given point in 3D space.
		/// </summary>
		/// <param name="zoomCenter">The point in 3D space around which the zoom is centered.</param>
		/// <param name="zoomFactor">The factor by which to zoom. Values greater than 0 zoom out; values smaller than 0 zoom in.</param>
		void Zoom(Vector3D zoomCenter, double zoomFactor);

		void EnableAutocenter();

		void DisableAutocenter();

		void AutoScaleAndCenter();
	}

	public interface IWorld
	{
		int CurrentBodyCount { get; }

		TimeSpan ToWorld(TimeSpan applicationTimeSpan);

		TimeSpan ToApplication(TimeSpan worldTimeSpan);

		void SetTimescale(double timeScale);

		void EnableElasticCollisions();

		void DisableElasticCollisions();

		void EnableClosedBoundaries();

		void DisableClosedBoundaries();

		IReadOnlyList<Body> GetBodies();
	}

	delegate void ApplyStateHandler(State state);

	public delegate State UpdateStateHandler(State state);

    #endregion

    [SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "<Pending>")]
    event ApplyStateHandler? ApplyState;

	[SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "<Pending>")]
	event UpdateStateHandler? UpdateState;

	IViewport Viewport { get; }

	IWorld World { get; }

	IReadOnlyList<BodyPreset> BodyPresets { get; }

	IReadOnlyList<EngineType> EngineTypes { get; }

	FrameDiagnostics FrameDiagnostics { get; }

	TimeSpan CurrentRuntime { get; }

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