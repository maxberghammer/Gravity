using System;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks
{
    [CPUUsageDiagnoser]
    public class EntityExtensionsBench
    {
        private Entity _e1 = null !;
        private Entity _e2 = null !;
        private Entity _far1 = null !;
        private Entity _far2 = null !;
        private (Vector2D? p1, Vector2D? p2) _cancelNear;
        private (Vector2D? p1, Vector2D? p2) _cancelFar;
        private (Vector2D? v1, Vector2D? v2) _collideNear;
        private (Vector2D? v1, Vector2D? v2) _collideFar;
        private sealed class DummyWorld : IWorld
        {
            public bool ClosedBoundaries => false;
            public bool ElasticCollisions => true;
            public IViewport Viewport { get; } = new DummyViewport
            {
                TopLeft = new Vector2D(-1000, -1000),
                BottomRight = new Vector2D(1000, 1000),
                Scale = 1
            };

            private sealed class DummyViewport : IViewport
            {
                public Vector2D TopLeft { get; set; }
                public Vector2D BottomRight { get; set; }
                public double Scale { get; set; }
            }
        }

        [GlobalSetup]
        public void Setup()
        {
            var w = new DummyWorld();
            _e1 = new Entity(new Vector2D(0, 0), 0.5, 2.0, new Vector2D(1, 0), Vector2D.Zero, w, Color.White, null, 0);
            _e2 = new Entity(new Vector2D(0.8, 0), 0.5, 3.0, new Vector2D(-1, 0), Vector2D.Zero, w, Color.White, null, 0);
            _far1 = new Entity(new Vector2D(10, 10), 0.5, 2.0, new Vector2D(1, 0), Vector2D.Zero, w, Color.White, null, 0);
            _far2 = new Entity(new Vector2D(-10, -10), 0.5, 3.0, new Vector2D(-1, 0), Vector2D.Zero, w, Color.White, null, 0);
        }

        [Benchmark]
        public void CancelOverlapNear() => _cancelNear = _e1.CancelOverlap(_e2);
        [Benchmark]
        public void CancelOverlapFar() => _cancelFar = _far1.CancelOverlap(_far2);
        [Benchmark]
        public void HandleCollisionNear() => _collideNear = _e1.HandleCollision(_e2, elastic: true);
        [Benchmark]
        public void HandleCollisionFar() => _collideFar = _far1.HandleCollision(_far2, elastic: true);
    }
}