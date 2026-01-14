using System;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Implementation.Adaptive;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks;

[MemoryDiagnoser]
[CPUUsageDiagnoser]
public class BarnesHutInsertBench
{
	private Body[] _bodies100 = null!;
	private Body[] _bodies1000 = null!;
	private Body[] _bodies10000 = null!;
	
	[GlobalSetup]
	public void Setup()
	{
		_bodies100 = CreateBodies(100);
		_bodies1000 = CreateBodies(1000);
		_bodies10000 = CreateBodies(10000);
	}
	
	private static Body[] CreateBodies(int count)
	{
		var bodies = new Body[count];
		
		for(var i = 0; i < bodies.Length; i++)
		{
			var x = RandomNumberGenerator.GetInt32(-500, 500);
			var y = RandomNumberGenerator.GetInt32(-500, 500);
			bodies[i] = new Body(
				new Vector2D(x, y),
				10.0,
				1e12,
				Vector2D.Zero,
				Vector2D.Zero,
				Color.White,
				null,
				0);
		}
		
		return bodies;
	}
	
	[Benchmark]
	public double Insert100Bodies()
		=> RunInsert(_bodies100);
	
	[Benchmark]
	public double Insert1000Bodies()
		=> RunInsert(_bodies1000);
	
	[Benchmark]
	public double Insert10000Bodies()
		=> RunInsert(_bodies10000);
	
	private static double RunInsert(Body[] bodies)
	{
		var tree = new BarnesHutTree(new Vector2D(-600, -600), new Vector2D(600, 600), 0.7, bodies.Length);
		tree.AddRange(bodies);
		tree.ComputeMassDistribution();
		var nodeCount = tree.NodeCount;
		tree.Release();
		return nodeCount;
	}
}