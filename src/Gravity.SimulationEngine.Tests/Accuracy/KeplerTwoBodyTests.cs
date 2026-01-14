using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine; // for Body, IWorld
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests.Accuracy;

[TestClass]
public sealed class KeplerTwoBodyTests
{
    private static (double Period, double SemiMajor) MeasureOrbit(ISimulationEngine engine, IWorld world, Body primary, Body satellite, TimeSpan dt, int maxSteps)
    {
        // Sample radii while advancing simulation
        var radii = new List<double>(maxSteps);
        for (var s = 0; s < maxSteps; s++)
        {
            engine.Simulate(world, dt);
            var r = (satellite.Position - primary.Position).Length;
            radii.Add(r);
        }

        // Detect two successive local minima (three-point check)
        int firstMin = -1, secondMin = -1;
        for (int i = 1; i < radii.Count - 1; i++)
        {
            var r0 = radii[i - 1];
            var r1 = radii[i];
            var r2 = radii[i + 1];
            if (r1 <= r0 && r1 <= r2)
            {
                if (firstMin < 0)
                {
                    firstMin = i;
                }
                else
                {
                    secondMin = i;
                    break;
                }
            }
        }

        if (firstMin < 0 || secondMin < 0)
            return (double.NaN, double.NaN);

        var period = (secondMin - firstMin) * dt.TotalSeconds;

        // Semi-major axis from perihelion/aphelion within the cycle window
        var rMin = double.PositiveInfinity;
        var rMax = 0.0;
        for (int i = firstMin; i <= secondMin; i++)
        {
            var r = radii[i];
            if (r < rMin) rMin = r;
            if (r > rMax) rMax = r;
        }
        var a = (rMin + rMax) * 0.5;

        return (period, a);
    }

    private static async Task AssertKeplerAsync(Factory.SimulationEngineType engineType, string resourcePath, int steps, double relPeriodTol, double relRadiusTol)
    {
        var engine = Factory.Create(engineType);
        (var world, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
        world = world.CreateMock();

        var bodies = world.GetBodies();
        // Select two bodies: primary (heavier) and satellite
        var top2 = bodies.OrderByDescending(b => b.m).Take(2).ToArray();
        if (top2.Length != 2)
            throw new AssertFailedException("Two-body resource expected.");
        var primary = top2[0];
        var satellite = top2[1];

        // Warmup
        for (var s = 0; s < steps; s++)
            engine.Simulate(world, dt);

        // Measure (sampling without extra rescan advance)
        var (periodMeasured, aMeasured) = MeasureOrbit(engine, world, primary, satellite, dt, maxSteps: steps);
        Assert.IsFalse(double.IsNaN(periodMeasured), "Failed to measure orbital period.");

        // Kepler period
        var mu = IWorld.G * (primary.m + satellite.m);
        var periodExpected = 2.0 * Math.PI * Math.Sqrt(Math.Pow(aMeasured, 3) / mu);
        var relPeriodErr = Math.Abs(periodMeasured - periodExpected) / periodExpected;

        // Radius stability
        var rNow = (satellite.Position - primary.Position).Length;
        var relRadiusErr = Math.Abs(rNow - aMeasured) / Math.Max(1e-9, aMeasured);

        // Use (tolerance, actual) order
        Assert.IsLessThanOrEqualTo(relPeriodTol, relPeriodErr, $"Period relative error {relPeriodErr} exceeds {relPeriodTol}");
        Assert.IsLessThanOrEqualTo(relRadiusTol, relRadiusErr, $"Radius relative error {relRadiusErr} exceeds {relRadiusTol}");
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task BarnesHutLeapfrogTwoBodyKeplerPeriodAccurate()
        => await AssertKeplerAsync(Factory.SimulationEngineType.BarnesHutWithLeapfrog, Mock.ResourcePaths.TwoBodiesSimulation, steps: 10000, relPeriodTol: 5e-3, relRadiusTol: 1e-2);

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task AdaptiveLeapfrogTwoBodyKeplerPeriodAccurate()
        => await AssertKeplerAsync(Factory.SimulationEngineType.Adaptive, Mock.ResourcePaths.TwoBodiesSimulation, steps: 10000, relPeriodTol: 1e-2, relRadiusTol: 2e-2);
}