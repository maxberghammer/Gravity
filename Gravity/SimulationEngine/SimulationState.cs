using System.Windows;

namespace Gravity.SimulationEngine
{
	internal class SimulationState
	{
		// ReSharper disable InconsistentNaming
		public SimulationState(Vector aPosition, Vector av, double am)
			// ReSharper restore InconsistentNaming
		{
			Position = aPosition;
			v = av;
			m = am;
		}

		public Vector Position { get; set; }

		// ReSharper disable once InconsistentNaming
		public Vector v { get; set; }

		// ReSharper disable once InconsistentNaming
		public double m { get; set; }
	}
}