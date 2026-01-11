using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
public class ThreeBodyStabilityTests
{
    private static Entity[] CreateThreeBodySetup(out double energy0)
    {
        var world = new WorldStub();
        // Three bodies: two equal masses orbiting, one lighter perturber
        var e1 = new Entity(new Vector2D(-1.0, 0.0), radius: 0.01, mass: 1.0, velocity: new Vector2D(0.0, +0.8), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
        var e2 = new Entity(new Vector2D(+1.0, 0.0), radius: 0.01, mass: 1.0, velocity: new Vector2D(0.0, -0.8), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
        var e3 = new Entity(new Vector2D(0.0, 1.5), radius: 0.01, mass: 0.2, velocity: new Vector2D(-0.6, 0.0), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
        var entities = new[] { e1, e2, e3 };
        energy0 = TotalEnergy(entities);
        return entities;
    }

    private static double TotalEnergy(Entity[] entities)
    {
        double Ek = entities.Sum(e => 0.5 * e.m * e.v.LengthSquared);
        double Ep = 0.0;
        for(int i=0;i<entities.Length;i++)
            for(int j=i+1;j<entities.Length;j++)
            {
                var d = (entities[j].Position - entities[i].Position).Length;
                Ep += -IWorld.G * entities[i].m * entities[j].m / d;
            }
        return Ek + Ep;
    }

    private static Vector2D TotalMomentum(Entity[] entities)
        => entities.Aggregate(Vector2D.Zero, (acc, e) => acc + e.p);

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [SuppressMessage("MSTest.Analyzers", "MSTEST0037", Justification = "Numeric comparison is explicit and clear")]
    [SuppressMessage("Naming", "CA1707", Justification = "Test naming style for readability")]
    public void LeapfrogThreeBodyStabilityBounds()
    {
        var entities = CreateThreeBodySetup(out var energy0);
        ISimulationEngine engine = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        var dt = TimeSpan.FromMilliseconds(10);
        var steps = 15000; // modest runtime within 30s
        var p0 = TotalMomentum(entities);
        for(int i=0;i<steps;i++) engine.Simulate(entities, dt);
        var energyDrift = Math.Abs((TotalEnergy(entities) - energy0) / energy0);
        var p = TotalMomentum(entities);
        var momentumDrift = (p - p0).Length;
        Assert.IsTrue(energyDrift <= 0.1, $"Energy drift too high: {energyDrift}");
        Assert.IsTrue(momentumDrift <= 1e-5, $"Momentum drift too high: {momentumDrift}");
        // Bodies should remain finite (no NaN/Infinity)
        for(int i=0;i<entities.Length;i++)
        {
            Assert.IsFalse(double.IsNaN(entities[i].Position.X) || double.IsNaN(entities[i].Position.Y));
            Assert.IsFalse(double.IsInfinity(entities[i].Position.X) || double.IsInfinity(entities[i].Position.Y));
        }
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [SuppressMessage("MSTest.Analyzers", "MSTEST0037", Justification = "Numeric comparison is explicit and clear")]
    [SuppressMessage("Naming", "CA1707", Justification = "Test naming style for readability")]
    public void RungeKuttaThreeBodyStabilityBounds()
    {
        var entities = CreateThreeBodySetup(out var energy0);
        ISimulationEngine engine = new BarnesHutSimulationEngine(new RungeKuttaIntegrator());
        var dt = TimeSpan.FromMilliseconds(10);
        var steps = 15000;
        var p0 = TotalMomentum(entities);
        for(int i=0;i<steps;i++) engine.Simulate(entities, dt);
        var energyDrift = Math.Abs((TotalEnergy(entities) - energy0) / energy0);
        var p = TotalMomentum(entities);
        var momentumDrift = (p - p0).Length;
        Assert.IsTrue(energyDrift <= 0.1, $"Energy drift too high: {energyDrift}");
        Assert.IsTrue(momentumDrift <= 1e-5, $"Momentum drift too high: {momentumDrift}");
        for(int i=0;i<entities.Length;i++)
        {
            Assert.IsFalse(double.IsNaN(entities[i].Position.X) || double.IsNaN(entities[i].Position.Y));
            Assert.IsFalse(double.IsInfinity(entities[i].Position.X) || double.IsInfinity(entities[i].Position.Y));
        }
    }

    private sealed class WorldStub : IWorld
    {
        public bool ClosedBoundaries => false;
        public bool ElasticCollisions => false;
        public IViewport Viewport => null!;
    }
}
