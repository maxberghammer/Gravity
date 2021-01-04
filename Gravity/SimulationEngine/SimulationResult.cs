using System.Windows;

namespace Gravity.SimulationEngine
{
	internal class SimulationResult
	{
		// ReSharper disable InconsistentNaming
		public SimulationResult(Vector av, Vector aa)
			// ReSharper restore InconsistentNaming
		{
			v = av;
			a = aa;
		}

		// ReSharper disable once InconsistentNaming
		public Vector v { get; set; }

		// ReSharper disable once InconsistentNaming
		public Vector a { get; set; }
	}
}