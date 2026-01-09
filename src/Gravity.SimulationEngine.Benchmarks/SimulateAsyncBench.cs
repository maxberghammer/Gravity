using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks
{
    [CPUUsageDiagnoser]
    [MemoryDiagnoser]
    public class SimulateAsyncBench
    {
        private ISimulationEngine _engine = null!;
        private Entity[] _entities = null!;
        private TimeSpan _dt;

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

        [Params(500, 2000, 5000)]
        public int Count { get; set; }

        [GlobalSetup]
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic pseudo-random is fine for benchmarking data generation")]
        public void Setup()
        {
            _engine = Factory.CreateBarnesHut();
            _dt = TimeSpan.FromMilliseconds(16.6); // ~60 FPS
            var world = new DummyWorld();
            var rnd = new Random(42);
            _entities = new Entity[Count];
            for (int i = 0; i < Count; i++)
            {
                var pos = new Vector2D(rnd.NextDouble() * 1000 - 500, rnd.NextDouble() * 1000 - 500);
                var vel = new Vector2D(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                _entities[i] = new Entity(pos, radius: 0.2, mass: rnd.NextDouble() * 10 + 1, velocity: vel, acceleration: Vector2D.Zero, world: world, fill: Color.White, stroke: null, strokeWidth: 0);
            }
        }

        [Benchmark]
        [SuppressMessage("Usage", "VSTHRD002:Synchronously waiting on tasks or awaiters may cause deadlocks", Justification = "Benchmark requires synchronous pattern; run on threadpool context")]
        public double SimulateStep()
        {
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                _engine.SimulateAsync(_entities, _dt).GetAwaiter().GetResult();
                for (int e = 0; e < _entities.Length; e++)
                {
                    sum += _entities[e].v.Length;
                }
            }
            return sum;
        }
    }
}