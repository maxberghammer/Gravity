// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class NullIntegrator : IIntegrator
{
	#region Implementation of IIntegrator

	/// <inheritdoc/>
	Tuple<int, int>[] IIntegrator.Integrate(Body[] entities, TimeSpan deltaTime, Func<Body[], Tuple<int, int>[]> processFunc)
	{
		var collisions = processFunc(entities);

		Integrate(entities, deltaTime);

		return collisions;
	}

	#endregion

	#region Implementation

	private static void Integrate(Body[] entities, TimeSpan h)
	{
		var dt = h.TotalSeconds;

		Parallel.For(0, entities.Length, i =>
										{
											var entity = entities[i];
											entity.v += dt * entity.a;
											entity.Position += dt * entity.v;
										});
	}

	#endregion
}