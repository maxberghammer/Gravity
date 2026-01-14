using System;
using System.Linq;

namespace Gravity.SimulationEngine.Implementation;

internal abstract class SimulationEngineBase : ISimulationEngine
{
	#region Implementation of ISimulationEngine

	ISimulationEngine.IDiagnostics ISimulationEngine.GetDiagnostics()
		=> Diagnostics;

	void ISimulationEngine.Simulate(IWorld world, TimeSpan deltaTime)
	{
		var bodies = world.GetBodies();

		if(bodies.Length == 0 ||
		   deltaTime <= TimeSpan.Zero)
			return;

		OnSimulate(world, bodies, deltaTime);

		if(!world.ClosedBoundaries)
			return;

		// Weltgrenzen behandeln
		foreach(var body in bodies.Where(e => !e.IsAbsorbed))
			HandleCollisionWithWorldBoundaries(world, body);
	}

	#endregion

	#region Implementation

	/// <summary>
	///     Behandelt die Überlappung zweier gegebener Objekte und liefert, falls eine Überlappung vorliegt, gegebenenfalls die
	///     neuen Positionen der beiden Objekte, so dass sie sich nicht mehr überlappen.
	/// </summary>
	protected static (Vector2D? Position1, Vector2D? Position2) CancelOverlap(Body body1, Body body2)
	{
		ArgumentNullException.ThrowIfNull(body1);
		ArgumentNullException.ThrowIfNull(body2);

		var dist = body1.Position - body2.Position;
		var minDistAbs = body1.r + body2.r;

		if(body1.m < body2.m)
			return (body2.Position + dist.Unit() * minDistAbs, null);

		if(body1.m > body2.m)
			return (null, body1.Position - dist.Unit() * minDistAbs);

		return (body2.Position + (dist + dist.Unit() * minDistAbs) / 2, body1.Position - (dist + dist.Unit() * minDistAbs) / 2);
	}

	/// <summary>
	///     Behandelt die Kollision zweier gegebener Objekte und liefert, falls eine Kollision vorliegt, gegebenenfalls die
	///     neuen Geschwindigkeiten der beiden Objekte.
	/// </summary>
	protected static (Vector2D? v1, Vector2D? v2) HandleCollision(Body body1, Body body2, bool elastic)
	{
		ArgumentNullException.ThrowIfNull(body1);
		ArgumentNullException.ThrowIfNull(body2);

		if(body1.IsAbsorbed ||
		   body2.IsAbsorbed)
			return (null, null);

		var dist = body1.Position - body2.Position;

		if(dist.Length >= body1.r + body2.r)
			return (null, null);

		if(elastic)
		{
			var temp = dist / (dist * dist);

			// Masse Objekt 1
			var m1 = body1.m;
			// Masse Objekt 2
			var m2 = body2.m;
			// Geschwindigkeitsvektor Objekt 1
			var v1 = body1.v;
			// Geschwindigkeitsvektor Objekt 2
			var v2 = body2.v;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1
			var vn1 = temp * (dist * v1);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2
			var vn2 = temp * (dist * v2);
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 1
			var vt1 = v1 - vn1;
			// Geschwindigkeitsvektor auf der Berührungstangente Objekt 2
			var vt2 = v2 - vn2;
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 1 so korrigieren, dass die Objekt-Massen mit einfließen
			var un1 = (m1 * vn1 + m2 * (2 * vn2 - vn1)) / (m1 + m2);
			// Geschwindigkeitsvektor auf der Stoßnormalen Objekt 2 so korrigieren, dass die Objekt-Massen mit einfließen
			var un2 = (m2 * vn2 + m1 * (2 * vn1 - vn2)) / (m1 + m2);

			v1 = un1 + vt1;
			v2 = un2 + vt2;

			return (v1, v2);
		}

		// Vollständig unelastischer Stoß (Vereinigung): Impulserhaltung
		var mSum = body1.m + body2.m;
		var vMerged = (body1.m * body1.v + body2.m * body2.v) / mSum;

		if(body2.m > body1.m)
		{
			body2.Absorb(body1);

			return (null, vMerged);
		}

		body1.Absorb(body2);

		return (vMerged, null);
	}

	protected Diagnostics Diagnostics { get; } = new();

	protected abstract void OnSimulate(IWorld world, Body[] bodies, TimeSpan deltaTime);

	private static void HandleCollisionWithWorldBoundaries(IWorld world, Body body)
	{
		var leftX = world.Viewport.TopLeft.X + body.r;
		var topY = world.Viewport.TopLeft.Y + body.r;
		var rightX = world.Viewport.BottomRight.X - body.r;
		var bottomY = world.Viewport.BottomRight.Y - body.r;

		var pos = body.Position;
		var v = body.v;

		if(pos.X < leftX)
		{
			v = new(-v.X, v.Y);
			pos = new(leftX, pos.Y);
		}
		else if(pos.X > rightX)
		{
			v = new(-v.X, v.Y);
			pos = new(rightX, pos.Y);
		}

		if(pos.Y < topY)
		{
			v = new(v.X, -v.Y);
			pos = new(pos.X, topY);
		}
		else if(pos.Y > bottomY)
		{
			v = new(v.X, -v.Y);
			pos = new(pos.X, bottomY);
		}

		body.v = v;
		body.Position = pos;
	}

	#endregion
}