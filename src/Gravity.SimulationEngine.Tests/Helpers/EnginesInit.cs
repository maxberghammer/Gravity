using System;
using Gravity.SimulationEngine;

namespace Gravity.SimulationEngine.Tests.Helpers
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
    internal static class EnginesInit
    {
        public static Entity[] CreateEntities10k()
        {
            var world = new WorldStub();
            double mass = 1_000_000_000_000d;
            double radius = 10d;
            var entities = new Entity[10_000];
            var rnd = new Random(42);
            var box = new Vector2D(200_000, 200_000);
            for(int i=0;i<entities.Length;i++)
            {
                var pos = new Vector2D((rnd.NextDouble()-0.5)*box.X, (rnd.NextDouble()-0.5)*box.Y);
                entities[i] = new Entity(pos, radius, mass, Vector2D.Zero, Vector2D.Zero, world, default, null, 0);
            }
            var com = Vector2D.Zero; double mSum = 0;
            for(int i=0;i<entities.Length;i++){ com += entities[i].m * entities[i].Position; mSum += entities[i].m; }
            com /= mSum;
            for(int i=0;i<entities.Length;i++)
            {
                var e = entities[i];
                var rvec = e.Position - com;
                var rlen = Math.Max(1.0, rvec.Length);
                var vdir = new Vector2D(-rvec.Y, rvec.X) / Math.Max(1.0, rvec.Length);
                var vmag = Math.Sqrt(IWorld.G * mSum / rlen) * 0.1;
                e.v = vmag * vdir;
            }
            return entities;
        }

        private sealed class WorldStub : IWorld
        {
            public bool ClosedBoundaries => false;
            public bool ElasticCollisions => false;
            public IViewport Viewport => new ViewportStub();
            private sealed class ViewportStub : IViewport
            {
                // Simulate Zoom(zoomFactor = +1) applied at center (0,0) from Scale=0 with viewport size ~1920x1009:
                // new world size = previousSize * 10 => 19200 x 10090
                public Vector2D TopLeft => new(-9600.0, -5045.0);
                public Vector2D BottomRight => new(9600.0, 5045.0);
            }
        }
    }
}
