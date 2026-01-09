using BenchmarkDotNet.Running;

namespace BenchmarkSuite1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Discover and run all benchmarks in this assembly
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
