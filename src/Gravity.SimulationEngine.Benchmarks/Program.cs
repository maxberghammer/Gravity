using BenchmarkDotNet.Running;

namespace Gravity.SimulationEngine.Benchmarks
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
