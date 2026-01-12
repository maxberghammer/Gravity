using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Implementation.Integrators;
using Microsoft.VSDiagnostics;
using Gravity.SimulationEngine.Benchmarks.Helpers;
using Gravity.SimulationEngine.Benchmarks.Common;

namespace Gravity.SimulationEngine.Benchmarks;

[Microsoft.VSDiagnostics.CPUUsageDiagnoser]
public class Engines10000BarnesHutRungeKuttaBench : Engines10000BaseBench
{
    protected override Factory.SimulationEngineType EngineType => Factory.SimulationEngineType.BarnesHutWithRungeKutta;
}
