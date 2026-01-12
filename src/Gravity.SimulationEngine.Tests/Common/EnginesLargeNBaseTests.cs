using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gravity.SimulationEngine.Tests.Helpers;

namespace Gravity.SimulationEngine.Tests.Common
{
    public abstract class EnginesLargeNBaseTests
    {
        protected abstract Factory.SimulationEngineType EngineType { get; }

        protected void Run10kEntities1000Steps()
        {
            var engine = Factory.Create(EngineType);
            var entities = EnginesInit.CreateEntities10k();
            var dt = TimeSpan.FromMilliseconds(10);
            for(int s=0;s<1000;s++) engine.Simulate(entities, dt);
            for(int i=0;i<entities.Length;i++)
            {
                Assert.IsFalse(double.IsNaN(entities[i].Position.X) || double.IsNaN(entities[i].Position.Y));
                Assert.IsFalse(double.IsInfinity(entities[i].Position.X) || double.IsInfinity(entities[i].Position.Y));
            }
        }
    }
}
