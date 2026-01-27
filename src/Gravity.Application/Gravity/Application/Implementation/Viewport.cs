using System;
using System.Linq;
using System.Numerics;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Serialization;

namespace Gravity.Application.Gravity.Application.Implementation;

internal class Viewport : IViewport,
						  IApplication.IViewport
{
	#region Fields

	private const double _snapAngle = Math.PI / 4; // 45 degrees
	private readonly World _world;
	private double _cameraPitch;
	private double _cameraYaw;
	private double _rawPitch;

	// Raw (unsnapped) rotation values for smooth snap behavior
	private double _rawYaw;
	private double _scale;

	#endregion

	#region Construction

	public Viewport(World world)
		=> _world = world;

	#endregion

	#region Interface

	/// <summary>
	/// Tests if a ray intersects a sphere and returns the distance to the intersection.
	/// Returns null if no intersection.
	/// </summary>
	public static double? RaySphereIntersect(Vector3D rayOrigin, Vector3D rayDir, Vector3D sphereCenter, double sphereRadius)
	{
		// Vector from ray origin to sphere center
		var oc = rayOrigin - sphereCenter;

		// Quadratic formula coefficients for ray-sphere intersection
		// |rayOrigin + t*rayDir - sphereCenter|^2 = radius^2
		var a = rayDir.X * rayDir.X + rayDir.Y * rayDir.Y + rayDir.Z * rayDir.Z;
		var b = 2.0 * (oc.X * rayDir.X + oc.Y * rayDir.Y + oc.Z * rayDir.Z);
		var c = oc.X * oc.X + oc.Y * oc.Y + oc.Z * oc.Z - sphereRadius * sphereRadius;

		var discriminant = b * b - 4 * a * c;

		if(discriminant < 0)
			return null; // No intersection

		// Return the nearest positive intersection
		var sqrtDisc = Math.Sqrt(discriminant);
		var t1 = (-b - sqrtDisc) / (2 * a);
		var t2 = (-b + sqrtDisc) / (2 * a);

		if(t1 > 0)
			return t1;
		if(t2 > 0)
			return t2;

		return null; // Both intersections behind ray origin
	}

	public Vector3D Center
		=> Size / 2 + TopLeft;

	public Vector3D Size
		=> BottomRight - TopLeft;

	/// <summary>
	/// Depth of the 3D viewport. Change this property to modify the depth calculation.
	/// Currently equals height, creating a width×height×height cuboid.
	/// </summary>
	public double Depth
		=> Size.Y; // <- Hier ändern, um andere Tiefenberechnung zu verwenden

	/// <summary>
	/// Returns the 3D size with proper depth (width, height, depth).
	/// Use this instead of Size when you need the actual 3D bounds.
	/// </summary>
	public Vector3D Size3D
		=> new(Size.X, Size.Y, Depth);

	public double ScaleFactor
		=> 1 / Math.Pow(10, _scale);

	public Vector3D TopLeft { get; private set; }

	public Vector3D BottomRight { get; private set; }

	public bool Autocenter { get; private set; }

	public State.ViewportState GetState()
		=> new(new(TopLeft.X, TopLeft.Y, TopLeft.Z),
			   new(BottomRight.X, BottomRight.Y, BottomRight.Z),
			   _scale,
			   Autocenter);

	public void ApplyState(State.ViewportState state)
	{
		TopLeft = new(state.TopLeft.X, state.TopLeft.Y, state.TopLeft.Z);
		BottomRight = new(state.BottomRight.X, state.BottomRight.Y, state.BottomRight.Z);
		_scale = state.Scale;
		Autocenter = state.Autocenter;
	}

	public void Reset()
		=> SetBoundsAroundCenter(Vector3D.Zero, Size3D);

	// ReSharper disable once UnusedMember.Global
	public void CenterTo(Body entity)
		=> SetBoundsAroundCenter(entity.Position, Size3D);

	/// <summary>
	/// Sets the viewport bounds centered around the given point, keeping the current size.
	/// </summary>
	public void SetCenter(Vector3D center)
		=> SetBoundsAroundCenter(center, Size3D);

	/// <summary>
	/// Gets the camera forward direction (the direction the camera is looking).
	/// With the current setup: X right, Y up, Z out of screen towards viewer.
	/// Camera looks in -Z direction (into the screen).
	/// </summary>
	public Vector3D GetCameraForward()
	{
		var cosYaw = Math.Cos(_cameraYaw);
		var sinYaw = Math.Sin(_cameraYaw);
		var cosPitch = Math.Cos(_cameraPitch);
		var sinPitch = Math.Sin(_cameraPitch);

		// Forward direction (into the screen, -Z in default view)
		// This matches the renderer's camera setup
		return new(-sinYaw * cosPitch, -sinPitch, -cosYaw * cosPitch);
	}

	/// <summary>
	/// Creates a ray from a viewport point for 3D picking.
	/// Returns the ray origin (on the near plane) and direction.
	/// </summary>
	public (Vector3D Origin, Vector3D Direction) GetPickingRay(Vector2 viewportPoint)
	{
		// Get the point on the center plane
		var pointOnPlane = ToWorld(viewportPoint);

		// Get camera forward direction
		var forward = GetCameraForward();

		// For orthographic projection, all rays are parallel to the forward direction
		// The origin is the point on the plane, offset backwards along the ray
		var rayOrigin = pointOnPlane - forward * (Depth * 10); // Start far behind

		return (rayOrigin, forward);
	}

	/// <summary>
	/// Sets the viewport bounds centered around the given point with the specified 3D size.
	/// The depth (Z) is automatically calculated from the height (Y) using CalculateDepthFromHeight.
	/// </summary>
	public void SetBoundsAroundCenter(Vector3D center, Vector3D size3D)
	{
		// Calculate depth from the NEW height, not the current Depth property
		var depth = CalculateDepthFromHeight(size3D.Y);
		var halfSize = new Vector3D(size3D.X / 2, size3D.Y / 2, depth / 2);
		TopLeft = center - halfSize;
		BottomRight = center + halfSize;
	}

	#endregion

	#region Implementation of IViewport

	/// <inheritdoc/>
	Vector3D IApplication.IViewport.CurrentCenter
		=> Center;

	/// <inheritdoc />
	double IApplication.IViewport.CurrentScale
		=> _scale;

	/// <inheritdoc/>
	double IApplication.IViewport.CurrentCameraDistance
		=> 1000;

	/// <inheritdoc/>
	double IApplication.IViewport.CurrentCameraYaw
		=> _cameraYaw;

	/// <inheritdoc/>
	double IApplication.IViewport.CurrentCameraPitch
		=> _cameraPitch;

	/// <inheritdoc/>
	Vector3D IViewport.TopLeft
		=> TopLeft;

	/// <inheritdoc/>
	Vector3D IViewport.BottomRight
		=> BottomRight;

	void IApplication.IViewport.Zoom(Vector3D zoomCenter, double zoomFactor)
	{
	var previousScaleFactor = ScaleFactor;
	var previousSize = Size3D;
	var previousCenter = Center;

	_scale = _scale + zoomFactor;

	var sizeRatio = previousScaleFactor / ScaleFactor;
	var newWidth = previousSize.X * sizeRatio;
	var newHeight = previousSize.Y * sizeRatio;
	var newDepth = CalculateDepthFromHeight(newHeight);
	var newSize = new Vector3D(newWidth, newHeight, newDepth);

	// Zoom to cursor: keep the zoom center point at the same screen position
	// Calculate the offset from center to zoom point, then scale it
	// This avoids precision issues with large absolute coordinates
	var offsetFromCenter = previousCenter - zoomCenter;
	var scaledOffset = offsetFromCenter * sizeRatio;
	var newCenter = zoomCenter + scaledOffset;

	SetBoundsAroundCenter(newCenter, newSize);
	}

	/// <inheritdoc/>
	void IApplication.IViewport.Resize(Vector3D newSize)
		=> SetBoundsAroundCenter(Center, newSize);

	/// <inheritdoc/>
	void IApplication.IViewport.EnableAutocenter()
		=> Autocenter = true;

	/// <inheritdoc/>
	void IApplication.IViewport.DisableAutocenter()
		=> Autocenter = false;

	/// <inheritdoc/>
	void IApplication.IViewport.AutoScaleAndCenter()
	{
		var bodies = _world.GetBodies();

		if(bodies.Count == 0)
			return;

		var previousSize = Size3D;
		var topLeft = new Vector3D(bodies.Min(e => e.Position.X - e.r),
								   bodies.Min(e => e.Position.Y - e.r),
								   bodies.Min(e => e.Position.Z - e.r));
		var bottomRight = new Vector3D(bodies.Max(e => e.Position.X + e.r),
									   bodies.Max(e => e.Position.Y + e.r),
									   bodies.Max(e => e.Position.Z + e.r));
		var center = topLeft + (bottomRight - topLeft) / 2;
		var newSize = bottomRight - topLeft;
		// Maintain aspect ratio (width:height)
		if(newSize.X / newSize.Y < previousSize.X / previousSize.Y)
			newSize = new(newSize.Y * previousSize.X / previousSize.Y, newSize.Y, newSize.Z);
		if(newSize.X / newSize.Y > previousSize.X / previousSize.Y)
			newSize = new(newSize.X, newSize.X * previousSize.Y / previousSize.X, newSize.Z);

		SetBoundsAroundCenter(center, newSize);

		_scale = Math.Max(newSize.X / previousSize.X, newSize.Y / previousSize.Y);
	}

	/// <inheritdoc/>
	void IApplication.IViewport.Scale(double scale)
		=> _scale = scale;

	/// <inheritdoc/>
	void IApplication.IViewport.RotateCamera(double deltaYaw, double deltaPitch, bool snap)
	{
		if(snap)
		{
			// Accumulate raw rotation
			_rawYaw += deltaYaw;
			_rawPitch += deltaPitch;

			// Snap to 45° increments
			_cameraYaw = SnapToAngle(_rawYaw, _snapAngle);
			_cameraPitch = SnapToAngle(_rawPitch, _snapAngle);
		}
		else
		{
			// Normal rotation
			_cameraYaw += deltaYaw;
			_cameraPitch += deltaPitch;

			// Keep raw values in sync
			_rawYaw = _cameraYaw;
			_rawPitch = _cameraPitch;
		}
	}

	/// <inheritdoc/>
	float IApplication.IViewport.ToViewport(double worldLength)
		=> (float)(worldLength * ScaleFactor);

	/// <summary>
	/// Converts a viewport point to world coordinates, taking camera rotation into account.
	/// The resulting point lies on a plane through the viewport center, perpendicular to the camera view direction.
	/// </summary>
	Vector3D IApplication.IViewport.ToWorld(Vector2 viewportPoint)
		=> ToWorld(viewportPoint);

	/// <inheritdoc/>
	double IApplication.IViewport.ToWorld(float viewportLength)
		=> ToWorld(viewportLength);

	/// <inheritdoc/>
	Vector2 IApplication.IViewport.ToViewport(Vector3D worldPoint)
	{
		// Calculate offset from center in world space
		var offsetWorld = worldPoint - Center;

		// Calculate camera basis vectors matching CreateLookAt (same as in ToWorld)
		var cosYaw = Math.Cos(_cameraYaw);
		var sinYaw = Math.Sin(_cameraYaw);
		var cosPitch = Math.Cos(_cameraPitch);
		var sinPitch = Math.Sin(_cameraPitch);

		// Right vector - matches CreateLookAt
		var rightX = cosYaw;
		var rightZ = -sinYaw;

		// Up vector - matches CreateLookAt
		var upX = -sinYaw * sinPitch;
		var upY = cosPitch;
		var upZ = -cosYaw * sinPitch;

		// Project onto camera right vector (dot product) -> screen X
		var screenX = offsetWorld.X * rightX + offsetWorld.Z * rightZ;

		// Project onto camera up vector (dot product), negated for screen Y
		var screenY = -(offsetWorld.X * upX + offsetWorld.Y * upY + offsetWorld.Z * upZ);

		// Convert to viewport coordinates
		var screenCenterX = Size.X * ScaleFactor / 2;
		var screenCenterY = Size.Y * ScaleFactor / 2;

		return Vector2.Create(screenCenterX + screenX * ScaleFactor, screenCenterY + screenY * ScaleFactor);
	}

	#endregion

	#region Implementation

	/// <summary>
	/// Calculates the depth for a given height. Override this method to change the depth calculation.
	/// </summary>
	private static double CalculateDepthFromHeight(double height)
		=> height; // <- Hier ändern, um andere Tiefenberechnung zu verwenden

	/// <summary>
	/// Snaps an angle to the nearest multiple of snapAngle
	/// </summary>
	private static double SnapToAngle(double angle, double snapAngle)
		=> Math.Round(angle / snapAngle) * snapAngle;

	private Vector3D ToWorld(Vector2 viewportPoint)
	{
		// Calculate offset from viewport center in screen space
		var screenCenterX = Size.X * ScaleFactor / 2;
		var screenCenterY = Size.Y * ScaleFactor / 2;
		var offsetX = (viewportPoint.X - screenCenterX) / ScaleFactor;
		var offsetY = (viewportPoint.Y - screenCenterY) / ScaleFactor;

		// Calculate camera basis vectors matching CreateLookAt calculation:
		// zaxis = normalize(-forward), xaxis = Cross(worldUp, zaxis), yaxis = Cross(zaxis, xaxis)
		var cosYaw = Math.Cos(_cameraYaw);
		var sinYaw = Math.Sin(_cameraYaw);
		var cosPitch = Math.Cos(_cameraPitch);
		var sinPitch = Math.Sin(_cameraPitch);

		// Right vector (camera X axis) - matches CreateLookAt: Cross(worldUp, zaxis)
		// At yaw=0: (1, 0, 0), at yaw=45°: (0.707, 0, -0.707)
		var rightX = cosYaw;
		var rightY = 0.0;
		var rightZ = -sinYaw;

		// Up vector (camera Y axis) - matches CreateLookAt: Cross(zaxis, xaxis)
		var upX = -sinYaw * sinPitch;
		var upY = cosPitch;
		var upZ = -cosYaw * sinPitch;

		// Calculate world position: center + offset in camera space
		// offsetX moves along Right, offsetY moves opposite to Up (screen Y is inverted)
		var worldX = Center.X + offsetX * rightX - offsetY * upX;
		var worldY = Center.Y + offsetX * rightY - offsetY * upY;
		var worldZ = Center.Z + offsetX * rightZ - offsetY * upZ;

		return new(worldX, worldY, worldZ);
	}

	private double ToWorld(float viewportLength)
		=> viewportLength / ScaleFactor;

	#endregion
}