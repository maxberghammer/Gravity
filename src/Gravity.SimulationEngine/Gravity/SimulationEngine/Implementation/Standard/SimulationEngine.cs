// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;
using Gravity.SimulationEngine.Implementation.Oversamplers;

namespace Gravity.SimulationEngine.Implementation.Standard;

internal sealed class SimulationEngine : SimulationEngineBase
{
	#region Construction

	public SimulationEngine(IIntegrator integrator, IOversampler oversampler)
		: base(integrator, oversampler)
	{
	}

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnComputeAccelerations(IWorld world, Body[] bodies)
	{
		// Kollisionen exakt erkennen und auflösen
		ResolveCollisions(world, bodies);

		// Beschleunigungen berechnen
		ComputeAccelerations(bodies);
	}

	/// <inheritdoc/>
	protected override void OnAfterSimulationStep(IWorld world, Body[] bodies)
		=> ResolveCollisions(world, bodies);

	private static void ComputeAccelerations(Body[] bodies)
	{
		var n = bodies.Length;

		if(n == 0)
			return;

		Parallel.For(0, n, i =>
						   {
							   var body = bodies[i];

							   body.a = CalculateGravity(body, bodies.Where(e => !ReferenceEquals(e, body)));
						   });
	}

	private static Vector2D CalculateGravity(Body body, IEnumerable<Body> others)
	{
		if(body.IsAbsorbed)
			return Vector2D.Zero;

		var g = Vector2D.Zero;

		foreach(var other in others.Where(e => !e.IsAbsorbed))
		{
			var dist = body.Position - other.Position;

			// Gravitationsbeschleunigung integrieren
			g += other.m * dist / Math.Pow(dist.LengthSquared, 1.5d);
		}

		return -IWorld.G * g;
	}

	#endregion
}