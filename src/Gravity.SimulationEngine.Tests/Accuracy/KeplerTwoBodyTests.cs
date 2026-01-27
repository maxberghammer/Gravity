using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// for Body, IWorld

namespace Gravity.SimulationEngine.Tests.Accuracy;

[TestClass]
public sealed class KeplerTwoBodyTests
{
	#region Interface

	// Tolerances are set to ~2x measured values to catch regressions
	// Standard: relPeriod=2.76e-4, relEnergy=6.35e-5, relAngular=1.10e-14
	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task StandardTwoBodyKeplerPeriodAccurate()
		=> await AssertKeplerAsync(Factory.SimulationEngineType.Standard, ResourcePaths.TwoBodiesSimulation, 10000,
								   5e-4, 1.5e-4, 5e-14);

	// BarnesHut: relPeriod=3.23e-4, relEnergy=2.51e-9, relAngular=7.26e-14
	[TestMethod]
	[Timeout(60000, CooperativeCancellation = true)]
	public async Task AdaptiveBarnesHutKeplerPeriodAccurate()
		=> await AssertKeplerAsync(Factory.SimulationEngineType.AdaptiveBarnesHut, ResourcePaths.TwoBodiesSimulation, 10000,
								   5e-4, 1e-8, 2e-13);

	// ParticleMesh: relPeriod=3.23e-4, relEnergy=2.51e-9, relAngular=8.01e-14 (uses Direct for 2 bodies)
	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task ParticleMeshKeplerPeriodAccurate()
		=> await AssertKeplerAsync(Factory.SimulationEngineType.AdaptiveParticleMesh, ResourcePaths.TwoBodiesSimulation, 10000,
								   5e-4, 1e-8, 2e-13);

	// FastMultipole: relPeriod=3.23e-4, relEnergy=2.51e-9, relAngular=8.01e-14 (uses Direct for 2 bodies)
	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task FastMultipoleKeplerPeriodAccurate()
		=> await AssertKeplerAsync(Factory.SimulationEngineType.AdaptiveFastMultipole, ResourcePaths.TwoBodiesSimulation, 10000,
								   5e-4, 1e-8, 2e-13);

	#endregion

	#region Implementation

	private static double ComputeTheoreticalPeriod(Body primary, Body satellite, double mu)
	{
		// Relative position and velocity
		var r = satellite.Position - primary.Position;
		var v = satellite.v - primary.v;

		var rMag = r.Length;
		var vSq = v.LengthSquared;

		// Specific orbital energy: E = v²/2 - μ/r
		var specificEnergy = 0.5 * vSq - mu / rMag;

		// Semi-major axis: a = -μ / (2E)
		var a = -mu / (2.0 * specificEnergy);

		// Kepler's third law: T = 2π √(a³/μ)
		return 2.0 * Math.PI * Math.Sqrt(Math.Pow(a, 3) / mu);
	}

	private static double ComputeSpecificEnergy(Body primary, Body satellite, double mu)
	{
		var r = satellite.Position - primary.Position;
		var v = satellite.v - primary.v;
		var rMag = r.Length;
		var vSq = v.LengthSquared;

		return 0.5 * vSq - mu / rMag;
	}

	private static double ComputeAngularMomentum(Body primary, Body satellite)
	{
		var r = satellite.Position - primary.Position;
		var v = satellite.v - primary.v;

		// For 2D: L_z = r_x * v_y - r_y * v_x
		return r.X * v.Y - r.Y * v.X;
	}

	private static (double Period, double SemiMajor) MeasureOrbit(ISimulationEngine engine,
																  IWorld world,
																  IViewport viewport,
																  Body primary,
																  Body satellite,
																  TimeSpan dt,
																  int maxSteps)
	{
		// Sample radii while advancing simulation
		var radii = new List<double>(maxSteps);

		for(var s = 0; s < maxSteps; s++)
		{
			engine.Simulate(world, viewport, dt);
			var r = (satellite.Position - primary.Position).Length;
			radii.Add(r);
		}

		// Detect two successive local minima (three-point check)
		int firstMin = -1,
			secondMin = -1;

		for(var i = 1; i < radii.Count - 1; i++)
		{
			var r0 = radii[i - 1];
			var r1 = radii[i];
			var r2 = radii[i + 1];

			if(r1 <= r0 &&
			   r1 <= r2)
			{
				if(firstMin < 0)
					firstMin = i;
				else
				{
					secondMin = i;

					break;
				}
			}
		}

		if(firstMin < 0 ||
		   secondMin < 0)
			return (double.NaN, double.NaN);

		var period = (secondMin - firstMin) * dt.TotalSeconds;

		// Semi-major axis from perihelion/aphelion within the cycle window
		var rMin = double.PositiveInfinity;
		var rMax = 0.0;

		for(var i = firstMin; i <= secondMin; i++)
		{
			var r = radii[i];
			if(r < rMin)
				rMin = r;
			if(r > rMax)
				rMax = r;
		}

		var a = (rMin + rMax) * 0.5;

		return (period, a);
	}

	private static async Task AssertKeplerAsync(Factory.SimulationEngineType engineType,
												string resourcePath,
												int steps,
												double relPeriodTol,
												double relEnergyTol,
												double relAngularMomentumTol)
	{
		var engine = Factory.Create(engineType);
		(var world, var viewport, var dt) = await IWorld.CreateFromJsonResourceAsync(resourcePath);
		world = world.CreateMock();

		var bodies = world.GetBodies();
		// Select two bodies: primary (heavier) and satellite
		var top2 = bodies.OrderByDescending(b => b.m).Take(2).ToArray();

		if(top2.Length != 2)
			throw new AssertFailedException("Two-body resource expected.");

		var primary = top2[0];
		var satellite = top2[1];

		// Compute theoretical values from initial conditions
		var mu = IWorld.G * (primary.m + satellite.m);
		var periodExpected = ComputeTheoreticalPeriod(primary, satellite, mu);
		var energyInitial = ComputeSpecificEnergy(primary, satellite, mu);
		var angularMomentumInitial = ComputeAngularMomentum(primary, satellite);

		// Warmup
		for(var s = 0; s < steps; s++)
			engine.Simulate(world, viewport, dt);

		// Measure period
		(var periodMeasured, var _) = MeasureOrbit(engine, world, viewport, primary, satellite, dt, steps);
		Assert.IsFalse(double.IsNaN(periodMeasured), "Failed to measure orbital period.");

		// Compute final energy and angular momentum
		var energyFinal = ComputeSpecificEnergy(primary, satellite, mu);
		var angularMomentumFinal = ComputeAngularMomentum(primary, satellite);

		// Relative errors
		var relPeriodErr = Math.Abs(periodMeasured - periodExpected) / periodExpected;
		var relEnergyErr = Math.Abs(energyFinal - energyInitial) / Math.Abs(energyInitial);
		var relAngularMomentumErr = Math.Abs(angularMomentumFinal - angularMomentumInitial) / Math.Abs(angularMomentumInitial);

		// Assertions
		Assert.IsLessThanOrEqualTo(relPeriodTol, relPeriodErr, $"Period relative error {relPeriodErr} exceeds {relPeriodTol}");
		Assert.IsLessThanOrEqualTo(relEnergyTol, relEnergyErr, $"Energy relative error {relEnergyErr} exceeds {relEnergyTol}");
		Assert.IsLessThanOrEqualTo(relAngularMomentumTol, relAngularMomentumErr, $"Angular momentum relative error {relAngularMomentumErr} exceeds {relAngularMomentumTol}");
	}

	#endregion
}