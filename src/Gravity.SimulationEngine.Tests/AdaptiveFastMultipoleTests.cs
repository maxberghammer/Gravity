using System.Diagnostics.CodeAnalysis;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

/// <summary>
/// Basic engine tests for Adaptive Fast Multipole Method (FMM) algorithm.
/// Inherits common tests from EngineTestsBase.
/// </summary>
[TestClass]
[SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
public class AdaptiveFastMultipoleTests : EngineTestsBase
{
	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveFastMultipole;
}
