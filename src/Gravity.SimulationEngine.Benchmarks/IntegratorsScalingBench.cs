using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks;

[CPUUsageDiagnoser]
public class IntegratorsScalingBench
{
    [Params(1000, 5000, 10000)]
    public int N { get; set; }

    private Entity[] _entitiesLf = Array.Empty<Entity>();
    private Entity[] _entitiesRk = Array.Empty<Entity>();
    private TimeSpan _dt;
    private ISimulationEngine _lfEngine = null!;
    private ISimulationEngine _rkEngine = null!;

    [GlobalSetup]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Benchmark data generation; not used for security.")]
    public void Setup()
    {
        var world = new WorldStub();
        var rand = new Random(42);
        _entitiesLf = new Entity[N];
        _entitiesRk = new Entity[N];
        for (int i = 0; i < N; i++)
        {
            // Random positions in a square and small random velocities (not for security)
            double x = (rand.NextDouble() - 0.5) * 100.0;
            double y = (rand.NextDouble() - 0.5) * 100.0;
            double vx = (rand.NextDouble() - 0.5) * 0.1;
            double vy = (rand.NextDouble() - 0.5) * 0.1;
            double m = 1.0;
            var eLf = new Entity(new Vector2D(x, y), radius: 0.0, mass: m, velocity: new Vector2D(vx, vy), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
            var eRk = new Entity(eLf);
            _entitiesLf[i] = eLf;
            _entitiesRk[i] = eRk;
        }

        _dt = TimeSpan.FromMilliseconds(10);
        _lfEngine = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        _rkEngine = new BarnesHutSimulationEngine(new RungeKuttaIntegrator());
    }

    [IterationSetup(Targets = new[] { nameof(LeapfrogSteps1000), nameof(RungeKuttaSteps1000) })]
    public void Reset()
    {
        for (int i = 0; i < N; i++)
        {
            _entitiesLf[i].Position = _entitiesRk[i].Position; // positions same initially
            _entitiesLf[i].v = _entitiesRk[i].v;
            _entitiesLf[i].a = Vector2D.Zero;
            _entitiesRk[i].a = Vector2D.Zero;
        }
    }

    [Benchmark]
    [SuppressMessage("Usage", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Benchmark requires synchronous pattern; run on threadpool context")]
    public double LeapfrogSteps1000()
    {
        double sum = 0;
        var entities = _entitiesLf;
        for (int i = 0; i < 1000; i++)
        {
            _lfEngine.Simulate(entities, _dt);
            for (int e = 0; e < entities.Length; e++)
                sum += entities[e].v.Length;
        }

        return sum;
    }

    [Benchmark]
    [SuppressMessage("Usage", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Benchmark requires synchronous pattern; run on threadpool context")]
    public double RungeKuttaSteps1000()
    {
        double sum = 0;
        var entities = _entitiesRk;
        for (int i = 0; i < 1000; i++)
        {
            _rkEngine.Simulate(entities, _dt);
            for (int e = 0; e < entities.Length; e++)
                sum += entities[e].v.Length;
        }

        return sum;
    }

    private sealed class WorldStub : IWorld
    {
        public bool ClosedBoundaries => false;
        public bool ElasticCollisions => false;
        public IViewport Viewport => null!;
    }
}