using System;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Implementation.Integrators;
using Gravity.SimulationEngine.Implementation.Oversamplers;

namespace Gravity.SimulationEngine.Implementation.Adaptive;

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
		=> ComputeAccelerations(bodies);

	/// <inheritdoc/>
	protected override void OnAfterSimulationStep(IWorld world, Body[] bodies)
		=> ResolveCollisions(world, bodies);

	private static double ComputeTheta(Body[] bodies, double l, double t, double r, double b)
	{
		var n = bodies.Length;
		var width = Math.Max(1e-12, r - l);
		var height = Math.Max(1e-12, b - t);
		var span = Math.Max(width, height);
		var minSep = double.PositiveInfinity;

		for(var i = 0; i < Math.Min(n, 32); i++)
		{
			for(var j = i + 1; j < Math.Min(n, i + 32); j++)
			{
				var d = (bodies[j].Position - bodies[i].Position).Length;
				if(d < minSep)
					minSep = d;
			}
		}

		if(double.IsInfinity(minSep) ||
		   minSep <= 0)
			minSep = span;
		var sepRatio = Math.Clamp(minSep / span, 0.0, 1.0);

		if(n <= 3)
			return 0.0;
		if(n <= 10)
			return 0.1;
		if(n <= 50)
			return 0.2;

		var baseTheta = 0.62;
		var k = 0.22;
		var raw = baseTheta + k * Math.Log10(Math.Max(1, n));
		raw *= 0.9 + 0.2 * sepRatio;

		return Math.Clamp(raw, 0.6, 1.0);
	}

	private void ComputeAccelerations(Body[] bodies)
	{
		var n = bodies.Length;

		if(n == 0)
			return;

		// Bounds bestimmen
		double l = double.PositiveInfinity,
			   t = double.PositiveInfinity,
			   r = double.NegativeInfinity,
			   b = double.NegativeInfinity;

		for(var i = 0; i < n; i++)
		{
			var p = bodies[i].Position;
			if(p.X < l)
				l = p.X;
			if(p.Y < t)
				t = p.Y;
			if(p.X > r)
				r = p.X;
			if(p.Y > b)
				b = p.Y;
		}

		if(double.IsInfinity(l) ||
		   double.IsInfinity(t) ||
		   double.IsInfinity(r) ||
		   double.IsInfinity(b))
		{
			l = t = -1.0;
			r = b = 1.0;
		}

		// Theta adaptiv wie in Barnes–Hut (inkl. Small-N-Overrides)
		var theta = ComputeTheta(bodies, l, t, r, b);

		var tree = new BarnesHutTree(new(l, t), new(r, b), theta, n);
		// Presort by Morton-order for better locality
		tree.AddRange(bodies);
		tree.ComputeMassDistribution();
		tree.CollectDiagnostics = false;
		// Update diagnostics locally
		Parallel.For(0, n, i => { bodies[i].a = tree.CalculateGravity(bodies[i]); });
		Diagnostics.SetField("Nodes", tree.NodeCount);
		Diagnostics.SetField("MaxDepth", tree.MaxDepthReached);
		Diagnostics.SetField("Visits", tree.TraversalVisitCount);

		tree.Release();
	}

	#endregion
}