using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Gravity.SimulationEngine.Benchmarks;

internal static class Program
{
	#region Implementation

	private static void Main(string[] args)
	{
		// Configure job with reduced iterations for long-running benchmarks
		var config = DefaultConfig.Instance
								  .AddJob(Job.Default
											 .WithWarmupCount(5)
											 .WithIterationCount(5));

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
	}

	#endregion
}