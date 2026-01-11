using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks
{
    [CPUUsageDiagnoser]
    [MemoryDiagnoser]
    [ShortRunJob] // reduce warmup/iteration count so benchmarks start faster
    public class IntegratorsComparisonBench
    {
        private Entity[] _entities = null !;
        private TimeSpan _dt;
        private ISimulationEngine _rkEngine = null !;
        private ISimulationEngine _lfEngine = null !;
        private sealed class DummyViewport : IViewport
        {
            public Vector2D TopLeft { get; set; } = new Vector2D(-1000, -1000);
            public Vector2D BottomRight { get; set; } = new Vector2D(1000, 1000);
            public double Scale { get; set; } = 1;
        }

        private sealed class DummyWorld : IWorld
        {
            public bool ClosedBoundaries => false;
            public bool ElasticCollisions => true;
            public IViewport Viewport { get; } = new DummyViewport();
        }

        [GlobalSetup]
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic pseudo-random is fine for benchmarking data generation")]
        public void Setup()
        {
            // TimeScale = 1 -> base dt ~ 1/DisplayFrequency; emulate ~60 FPS dt
            _dt = TimeSpan.FromMilliseconds(16.6667);
            var world = new DummyWorld();
            var rnd = new Random(42);
            _entities = new Entity[10000];
            for (int i = 0; i < _entities.Length; i++)
            {
                // Preset: "Mittelschwer+Klein" => m = 100000000000, r = 20
                var pos = new Vector2D(rnd.NextDouble() * 2000 - 1000, rnd.NextDouble() * 2000 - 1000);
                var vel = new Vector2D(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                _entities[i] = new Entity(pos, radius: 20, mass: 100000000000d, velocity: vel, acceleration: Vector2D.Zero, world: world, fill: Color.Green, stroke: null, strokeWidth: 0);
            }

            _rkEngine = new BarnesHutSimulationEngine(new RungeKuttaIntegrator(substeps: 1));
            _lfEngine = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        }

        [Benchmark]
        [SuppressMessage("Usage", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Benchmark requires synchronous pattern; run on threadpool context")]
        public double RungeKuttaSteps1000()
        {
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                _rkEngine.Simulate(_entities, _dt);
                for (int e = 0; e < _entities.Length; e++)
                {
                    sum += _entities[e].v.Length;
                }
            }

            return sum;
        }

        [Benchmark]
        [SuppressMessage("Usage", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Benchmark requires synchronous pattern; run on threadpool context")]
        public double LeapfrogSteps1000()
        {
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                _lfEngine.Simulate(_entities, _dt);
                for (int e = 0; e < _entities.Length; e++)
                {
                    sum += _entities[e].v.Length;
                }
            }

            return sum;
        }
    }
}