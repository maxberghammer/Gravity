using System.Diagnostics.CodeAnalysis;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
[SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
public sealed class AdaptiveDirectTests : EngineTestsBase
{
	#region Implementation

	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.AdaptiveDirect;

	#endregion
}
