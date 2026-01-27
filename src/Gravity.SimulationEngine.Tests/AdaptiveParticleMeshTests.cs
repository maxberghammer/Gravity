using System.Diagnostics.CodeAnalysis;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

/// <summary>
/// Basic engine tests for Adaptive Particle-Mesh (PM) algorithm.
/// Inherits common tests from EngineTestsBase.
/// </summary>
[TestClass]
[SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
public class AdaptiveParticleMeshTests : EngineTestsBase
{
	#region Implementation

	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveParticleMesh;

	#endregion
}