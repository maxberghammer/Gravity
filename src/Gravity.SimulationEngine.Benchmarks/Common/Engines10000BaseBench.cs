using System;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine.Benchmarks.Helpers;

namespace Gravity.SimulationEngine.Benchmarks.Common
{
    public abstract class Engines10000BaseBench
    {
        private Entity[] _entities = Array.Empty<Entity>();
        private ISimulationEngine _engine = null!;
        private TimeSpan _dt;

        protected abstract Factory.SimulationEngineType EngineType { get; }

        [GlobalSetup]
        public void Setup()
        {
            _entities = EnginesInit.CreateEntities10k();
            _engine = Factory.Create(EngineType);
            _dt = TimeSpan.FromMilliseconds(10);
        }

        [Benchmark]
        public double Simulate1000Steps()
        {
            double sum = 0;
            for(int i=0;i<1000;i++)
            {
                _engine.Simulate(_entities, _dt);
                for(int e=0;e<_entities.Length;e++) sum += _entities[e].v.Length;
            }
            return sum;
        }
    }
}
