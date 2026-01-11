using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
public class TwoBodyPhysicsTests
{
    private static Entity[] CreateTwoBodyCircular(out double initialRadius)
    {
        var world = new WorldStub();
        var G = IWorld.G;
        double m1 = 1.0, m2 = 1.0;
        double r = 1.0; // bodies at +/-r on x-axis
        double M = m1 + m2;
        double v = Math.Sqrt(G * M / (2 * r));
        var e1 = new Entity(new Vector2D(-r, 0), radius: 0.01, mass: m1, velocity: new Vector2D(0, +v), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
        var e2 = new Entity(new Vector2D(+r, 0), radius: 0.01, mass: m2, velocity: new Vector2D(0, -v), acceleration: Vector2D.Zero, world: world, fill: default, stroke: null, strokeWidth: 0);
        initialRadius = (e2.Position - e1.Position).Length;
        return new[] { e1, e2 };
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
    public void LeapfrogAccuracyWithinTolerance()
    {
        var entities = CreateTwoBodyCircular(out var r0);
        ISimulationEngine engine = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        var dt = TimeSpan.FromMilliseconds(10);
        var steps = 10000; // ~100 s sim-time, runs fast; keep under 30 s wall time
        var energy0 = TotalEnergy(entities);
        for(int i=0;i<steps;i++) engine.Simulate(entities, dt);
        var energyDrift = Math.Abs((TotalEnergy(entities) - energy0) / energy0);
        var r = (entities[1].Position - entities[0].Position).Length;
        var radiusDrift = Math.Abs((r - r0) / r0);
        Assert.IsTrue(energyDrift <= 0.05, $"Leapfrog energy drift too high: {energyDrift}");
        Assert.IsTrue(radiusDrift <= 0.05, $"Leapfrog radius drift too high: {radiusDrift}");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [SuppressMessage("MSTest.Analyzers", "MSTEST0037", Justification = "Numeric comparison is explicit and clear")]
    [SuppressMessage("Naming", "CA1707", Justification = "Test naming style for readability")]
    public void TwoBodyPhysicalCorrectnessMomentumConservation()
    {
        var entities = CreateTwoBodyCircular(out _);
        ISimulationEngine engine = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        var dt = TimeSpan.FromMilliseconds(10);
        var steps = 10000;
        var p0 = TotalMomentum(entities);
        for(int i=0;i<steps;i++) engine.Simulate(entities, dt);
        var p = TotalMomentum(entities);
        var deltaP = (p - p0).Length;
        var tol = 1e-6; // numerical tolerance
        Assert.IsTrue(deltaP <= tol, $"Momentum not conserved: |?p|={deltaP}");
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [SuppressMessage("MSTest.Analyzers", "MSTEST0037", Justification = "Numeric comparison is explicit and clear")]
    [SuppressMessage("Naming", "CA1707", Justification = "Test naming style for readability")]
    public void RungeKuttaVsLeapfrogAccuracyComparison()
    {
        var lfEntities = CreateTwoBodyCircular(out var r0);
        var rkEntities = lfEntities.Select(e => new Entity(e)).ToArray();
        ISimulationEngine lf = new BarnesHutSimulationEngine(new LeapfrogIntegrator());
        ISimulationEngine rk = new BarnesHutSimulationEngine(new RungeKuttaIntegrator());
        var dt = TimeSpan.FromMilliseconds(10);
        var steps = 10000;
        var energy0 = TotalEnergy(lfEntities);
        for(int i=0;i<steps;i++) lf.Simulate(lfEntities, dt);
        for(int i=0;i<steps;i++) rk.Simulate(rkEntities, dt);
        var lfEnergyDrift = Math.Abs((TotalEnergy(lfEntities) - energy0) / energy0);
        var rkEnergyDrift = Math.Abs((TotalEnergy(rkEntities) - energy0) / energy0);
        var lfRadiusDrift = Math.Abs(((lfEntities[1].Position - lfEntities[0].Position).Length - r0) / r0);
        var rkRadiusDrift = Math.Abs(((rkEntities[1].Position - rkEntities[0].Position).Length - r0) / r0);
        Assert.IsTrue(rkEnergyDrift <= lfEnergyDrift + 0.01, $"RK energy drift {rkEnergyDrift} worse than LF {lfEnergyDrift}");
        Assert.IsTrue(rkRadiusDrift <= lfRadiusDrift + 0.01, $"RK radius drift {rkRadiusDrift} worse than LF {lfRadiusDrift}");
    }

    private sealed class WorldStub : IWorld
    {
        public bool ClosedBoundaries => false;
        public bool ElasticCollisions => false;
        public IViewport Viewport => null!;
    }
}
