using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Computations;

internal sealed class Direct : SimulationEngine.IComputation
{
	#region Fields

	private readonly SimulationEngine.ICollisionResolver _collisionResolver;

	#endregion

	#region Construction

	public Direct(SimulationEngine.ICollisionResolver collisionResolver)
		=> _collisionResolver = collisionResolver;

	#endregion

	#region Implementation of IComputation

	/// <inheritdoc/>
	void SimulationEngine.IComputation.Compute(IWorld world, IReadOnlyList<Body> allBodies, IReadOnlyList<Body> bodiesToUpdate, Diagnostics diagnostics)
	{
		// Kollisionen exakt erkennen und auflösen
		_collisionResolver.ResolveCollisions(world, allBodies, diagnostics);

		// Beschleunigungen berechnen
		ComputeAccelerations(allBodies, bodiesToUpdate);
	}

	#endregion

	#region Implementation

	private static void ComputeAccelerations(IReadOnlyList<Body> allBodies, IReadOnlyList<Body> bodiesToUpdate)
	{
		var n = bodiesToUpdate.Count;

		if(n == 0)
			return;

		Parallel.For(0, n, i =>
						   {
							   var body = bodiesToUpdate[i];

							   body.a = CalculateGravity(body, allBodies.Where(e => !ReferenceEquals(e, body)));
						   });
	}

	private static Vector3D CalculateGravity(Body body, IEnumerable<Body> others)
	{
		if(body.IsAbsorbed)
			return Vector3D.Zero;

		var g = Vector3D.Zero;
		var bodyR = body.r;

		foreach(var other in others.Where(e => !e.IsAbsorbed))
		{
			var dist = body.Position - other.Position;
			var distSq = dist.LengthSquared;

			// Prevent singularity: clamp distance to sum of radii
			// Bodies closer than this should collide anyway
			var minDist = bodyR + other.r;
			var minDistSq = minDist * minDist;
			if(distSq < minDistSq)
				distSq = minDistSq;

			g += other.m * dist / Math.Pow(distSq, 1.5d);
		}

		return -IWorld.G * g;
	}

	#endregion
}