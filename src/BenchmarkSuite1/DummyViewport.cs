using Gravity.SimulationEngine;

namespace Gravity.SimulationEngine.Benchmarks
{
    internal sealed class DummyViewport : IViewport
    {
        public Vector2D TopLeft { get; set; }
        public Vector2D BottomRight { get; set; }
        public double Scale { get; set; }
    }
}
