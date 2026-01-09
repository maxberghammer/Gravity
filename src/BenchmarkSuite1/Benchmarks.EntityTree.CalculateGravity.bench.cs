using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;

namespace Gravity.SimulationEngine.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10, launchCount: 1, id: "DefaultJob")]
    public class EntityTreeCalculateGravityBench
    {
        [Params(0.3, 0.5, 0.8)]
        public double Theta { get; set; }

        private EntityTree _tree = null!;
        private Entity _target = null!;
        private Entity[] _entities = null!;

        private sealed class DummyWorld : IWorld
        {
            public bool ClosedBoundaries => false;
            public bool ElasticCollisions => true;
            public IViewport Viewport { get; } = new DummyViewport { TopLeft = new Vector2D(-1000, -1000), BottomRight = new Vector2D(1000, 1000), Scale = 1 };
        }

        [GlobalSetup]
        public void Setup()
        {
            var rnd = new Random(42);
            int count = 2000; // typical scene size
            _entities = new Entity[count];
            var world = new DummyWorld();
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector2D(rnd.NextDouble() * 1000 - 500, rnd.NextDouble() * 1000 - 500);
                var vel = new Vector2D(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                _entities[i] = new Entity(pos, radius: 0.2, mass: rnd.NextDouble() * 10 + 1, velocity: vel, acceleration: Vector2D.Zero, world: world, fill: Color.White, stroke: null, strokeWidth: 0);
            }

            _target = new Entity(new Vector2D(250, 250), radius: 0.2, mass: 5, velocity: Vector2D.Zero, acceleration: Vector2D.Zero, world: world, fill: Color.White, stroke: null, strokeWidth: 0);

            _tree = new EntityTree(new Vector2D(-500, -500), new Vector2D(500, 500), theta: Theta);
            foreach (var e in _entities) _tree.Add(e);
            _tree.ComputeMassDistribution();
        }

        [Benchmark]
        public double CalculateGravityOnTarget()
        {
            // Aggregate multiple calls to ensure stable measurement
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                sum += _tree.CalculateGravity(_target).Length;
            }
            return sum;
        }
    }
}