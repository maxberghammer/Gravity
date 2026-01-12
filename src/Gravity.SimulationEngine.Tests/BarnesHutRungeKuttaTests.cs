using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Mock;
using Gravity.SimulationEngine.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gravity.SimulationEngine.Tests;

[TestClass]
[SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
public class BarnesHutRungeKuttaTests : EngineTestsBase
{

	#region Implementation

	protected override Factory.SimulationEngineType EngineType
		=> Factory.SimulationEngineType.BarnesHutWithRungeKutta;

	#endregion
}