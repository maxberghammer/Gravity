using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gravity.SimulationEngine;
using Gravity.SimulationEngine.Implementation;
using Gravity.SimulationEngine.Tests.Helpers;
using Gravity.SimulationEngine.Tests.Common;

namespace Gravity.SimulationEngine.Tests
{
    [TestClass]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394", Justification = "Non-security test data generation")]
    public class EnginesLargeNStandardTests : EnginesLargeNBaseTests
    {
        protected override Factory.SimulationEngineType EngineType => Factory.SimulationEngineType.Standard;

        [TestMethod]
        [Timeout(60000, CooperativeCancellation = true)]
        public void StandardRun10kEntities1000Steps()
            => Run10kEntities1000Steps();
    }
}
