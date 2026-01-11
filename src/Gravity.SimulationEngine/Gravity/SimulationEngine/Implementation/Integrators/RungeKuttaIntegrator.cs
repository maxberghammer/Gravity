// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Integrators;

internal sealed class RungeKuttaIntegrator : IIntegrator
{
	#region Fields

	private readonly int _substeps;

	#endregion

	#region Construction

	public RungeKuttaIntegrator(int substeps = 1)
	{
		if(substeps < 1)
			throw new ArgumentOutOfRangeException(nameof(substeps));

		_substeps = substeps;
	}

	#endregion

	#region Implementation of IIntegrator

	Tuple<int, int>[] IIntegrator.Integrate(Entity[] entities, TimeSpan deltaTime, Func<Entity[], Tuple<int, int>[]> processFunc)
	{
		var n = entities.Length;

		if(n == 0)
			return Array.Empty<Tuple<int, int>>();

		var dt = deltaTime.TotalSeconds;
		var dts = dt / _substeps;

		// Zustands-Puffer aus ArrayPool (einmal pro Integrationsaufruf)
		var pool = ArrayPool<Vector2D>.Shared;
		var x0 = pool.Rent(n);
		var v0 = pool.Rent(n);
		var k1x = pool.Rent(n);
		var k1v = pool.Rent(n);
		var k2x = pool.Rent(n);
		var k2v = pool.Rent(n);
		var k3x = pool.Rent(n);
		var k3v = pool.Rent(n);
		var k4x = pool.Rent(n);
		var k4v = pool.Rent(n);

		// Kollisionssammler über alle Substeps + Stufen
		var collisionsSet = new HashSet<long>(256);
		var collisionsAll = new List<Tuple<int, int>>(256);

		try
		{
			for(var s = 0; s < _substeps; s++)
			{
				var h2 = 0.5d * dts;
				var h6 = dts / 6.0d;

				// Startzustand dieses Substeps sichern
				Parallel.For(0, n, i =>
							   {
								   x0[i] = entities[i].Position;
								   v0[i] = entities[i].v;
							   });

				// Stufe 1 (t): a = a(t, x0, v0)
				var collisions1 = processFunc(entities);
				Parallel.For(0, n, i =>
							   {
								   k1x[i] = v0[i];
								   k1v[i] = entities[i].a;

								   // Eingang Stufe 2
								   entities[i].Position = x0[i] + h2 * k1x[i];
								   entities[i].v = v0[i] + h2 * k1v[i];
							   });

				// Stufe 2 (t + dt/2)
				var collisions2 = processFunc(entities);
				Parallel.For(0, n, i =>
							   {
								   k2x[i] = entities[i].v;
								   k2v[i] = entities[i].a;

								   // Eingang Stufe 3
								   entities[i].Position = x0[i] + h2 * k2x[i];
								   entities[i].v = v0[i] + h2 * k2v[i];
							   });

				// Stufe 3 (t + dt/2)
				var collisions3 = processFunc(entities);
				Parallel.For(0, n, i =>
							   {
								   k3x[i] = entities[i].v;
								   k3v[i] = entities[i].a;

								   // Eingang Stufe 4
								   entities[i].Position = x0[i] + dts * k3x[i];
								   entities[i].v = v0[i] + dts * k3v[i];
							   });

				// Stufe 4 (t + dt)
				var collisions4 = processFunc(entities);
				Parallel.For(0, n, i =>
							   {
								   k4x[i] = entities[i].v;
								   k4v[i] = entities[i].a;
							   });

				// Endzustand dieses Substeps setzen
				Parallel.For(0, n, i =>
							   {
								   var v = v0[i] + h6 * (k1v[i] + 2.0d * (k2v[i] + k3v[i]) + k4v[i]);
								   var x = x0[i] + h6 * (k1x[i] + 2.0d * (k2x[i] + k3x[i]) + k4x[i]);

								   entities[i].v = v;
								   entities[i].Position = x;
								   entities[i].a = k4v[i]; // Näherung; wird vor nächstem Substep neu berechnet
							   });

				// Kollisionen dieses Substeps de-duplizieren und sammeln
				AddCollisions(collisionsSet, collisionsAll, collisions1);
				AddCollisions(collisionsSet, collisionsAll, collisions2);
				AddCollisions(collisionsSet, collisionsAll, collisions3);
				AddCollisions(collisionsSet, collisionsAll, collisions4);
			}

			return collisionsAll.Count == 0
				   ? Array.Empty<Tuple<int, int>>()
				   : collisionsAll.ToArray();
		}
		finally
		{
			// Arrays an den Pool zurückgeben
			pool.Return(x0, clearArray: false);
			pool.Return(v0, clearArray: false);
			pool.Return(k1x, clearArray: false);
			pool.Return(k1v, clearArray: false);
			pool.Return(k2x, clearArray: false);
			pool.Return(k2v, clearArray: false);
			pool.Return(k3x, clearArray: false);
			pool.Return(k3v, clearArray: false);
			pool.Return(k4x, clearArray: false);
			pool.Return(k4v, clearArray: false);
		}
	}

	#endregion

	#region Implementation

	private static void AddCollisions(HashSet<long> set, List<Tuple<int, int>> list, Tuple<int, int>[] src)
	{
		for(var i = 0; i < src.Length; i++)
		{
			var a = src[i].Item1;
			var b = src[i].Item2;
			var min = a < b
					  ? a
					  : b;
			var max = a < b
					  ? b
					  : a;
			var key = ((long)min << 32) | (uint)max;
			if(set.Add(key))
				list.Add(Tuple.Create(min, max));
		}
	}

	#endregion
}