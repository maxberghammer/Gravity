using System.Diagnostics.CodeAnalysis;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

/// <summary>
/// Basic engine tests for Particle-Mesh (PM) algorithm.
/// Inherits common tests from EngineTestsBase.
/// </summary>
[TestClass]
[SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
public class ParticleMeshTests : EngineTestsBase
{
	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveParticleMesh;
}
