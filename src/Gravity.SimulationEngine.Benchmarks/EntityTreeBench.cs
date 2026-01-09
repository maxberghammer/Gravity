using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;

namespace Gravity.SimulationEngine.Benchmarks
{
    [MemoryDiagnoser]
    public class EntityTreeBench
    {
        private EntityTree _tree = null!;
        private Entity _target = null!;

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

        [Params(0.5, 0.8)]
        public double Theta { get; set; }

        [GlobalSetup]
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic pseudo-random is fine for benchmarking data generation")]
        public void Setup()
        {
            var rnd = new Random(42);
            int count = 2000;
            var entities = new Entity[count];
            var world = new DummyWorld();
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector2D(rnd.NextDouble() * 1000 - 500, rnd.NextDouble() * 1000 - 500);
                var vel = new Vector2D(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                entities[i] = new Entity(pos, radius: 0.2, mass: rnd.NextDouble() * 10 + 1, velocity: vel, acceleration: Vector2D.Zero, world: world, fill: Color.White, stroke: null, strokeWidth: 0);
            }
            _target = new Entity(new Vector2D(250, 250), radius: 0.2, mass: 5, velocity: Vector2D.Zero, acceleration: Vector2D.Zero, world: world, fill: Color.White, stroke: null, strokeWidth: 0);

            _tree = new EntityTree(new Vector2D(-500, -500), new Vector2D(500, 500), theta: Theta);
            for (int i = 0; i < entities.Length; i++) _tree.Add(entities[i]);
            _tree.ComputeMassDistribution();
        }

        [Benchmark]
        public double CalculateGravityOnTarget()
        {
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += _tree.CalculateGravity(_target).Length;
            }
            return sum;
        }
    }
}
