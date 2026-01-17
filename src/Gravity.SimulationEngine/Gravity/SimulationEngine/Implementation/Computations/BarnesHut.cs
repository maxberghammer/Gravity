using System;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Implementation.Computations;

/// <summary>
/// Barnes-Hut tree-based computation
/// O(N log N) complexity with adaptive theta parameter
/// </summary>
internal sealed partial class BarnesHut : SimulationEngine.IComputation
{
	#region Implementation of IComputation

	/// <inheritdoc/>
	void SimulationEngine.IComputation.Compute(IWorld world, Body[] bodies, Diagnostics diagnostics)
	{
		var n = bodies.Length;

		if(n == 0)
			return;

		// Bounds bestimmen (3D)
		double minX = double.PositiveInfinity,
			   minY = double.PositiveInfinity,
			   minZ = double.PositiveInfinity,
			   maxX = double.NegativeInfinity,
			   maxY = double.NegativeInfinity,
			   maxZ = double.NegativeInfinity;

		for(var i = 0; i < n; i++)
		{
			var p = bodies[i].Position;
			if(p.X < minX)
				minX = p.X;
			if(p.Y < minY)
				minY = p.Y;
			if(p.Z < minZ)
				minZ = p.Z;
			if(p.X > maxX)
				maxX = p.X;
			if(p.Y > maxY)
				maxY = p.Y;
			if(p.Z > maxZ)
				maxZ = p.Z;
		}

		if(double.IsInfinity(minX) ||
		   double.IsInfinity(minY) ||
		   double.IsInfinity(minZ) ||
		   double.IsInfinity(maxX) ||
		   double.IsInfinity(maxY) ||
		   double.IsInfinity(maxZ))
		{
			minX = minY = minZ = -1.0;
			maxX = maxY = maxZ = 1.0;
		}

		// Theta adaptiv wie in Barnes–Hut (inkl. Small-N-Overrides)
		var theta = ComputeTheta(bodies, minX, minY, minZ, maxX, maxY, maxZ);

		var tree = new Tree(new(minX, minY, minZ), new(maxX, maxY, maxZ), theta, n);
		// Presort by Morton-order for better locality
		tree.AddRange(bodies);
		tree.ComputeMassDistribution();
		tree.CollectDiagnostics = false;
		// Update diagnostics locally
		Parallel.For(0, n, i => { bodies[i].a = tree.CalculateGravity(bodies[i]); });

		diagnostics.SetField("Strategy", "Barnes-Hut");
		diagnostics.SetField("Nodes", tree.NodeCount);
		diagnostics.SetField("MaxDepth", tree.MaxDepthReached);
		diagnostics.SetField("Visits", tree.TraversalVisitCount);
		diagnostics.SetField("Theta", theta);

		tree.Release();
	}

	#endregion

	#region Implementation

	private static double ComputeTheta(Body[] bodies, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
	{
		var n = bodies.Length;
		var width = Math.Max(1e-12, maxX - minX);
		var height = Math.Max(1e-12, maxY - minY);
		var depth = Math.Max(1e-12, maxZ - minZ);
		var span = Math.Max(width, Math.Max(height, depth));
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

	#endregion
}