using System;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Benchmarks.Helpers;
using Gravity.SimulationEngine.Benchmarks.Common;
using Microsoft.VSDiagnostics;

namespace Gravity.SimulationEngine.Benchmarks;

[Microsoft.VSDiagnostics.CPUUsageDiagnoser]
public class Engines10000AdaptiveBench : Engines10000BaseBench
{
    protected override Factory.SimulationEngineType EngineType => Factory.SimulationEngineType.Adaptive;
}
