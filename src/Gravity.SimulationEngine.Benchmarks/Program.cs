using BenchmarkDotNet.Running;
using System;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Gravity.SimulationEngine.Benchmarks;

internal static class Program
{
	#region Implementation

	private static void Main(string[] args)
	{
		var resArg = args.FirstOrDefault(a => a.StartsWith("--resource=", StringComparison.OrdinalIgnoreCase));

		if(resArg != null)
		{
			var v = resArg.Substring("--resource=".Length);
			BenchParams.ResourcePath = string.IsNullOrWhiteSpace(v)
								   ? null
								   : v;
		}

		var stepsArg = args.FirstOrDefault(a => a.StartsWith("--steps=", StringComparison.OrdinalIgnoreCase));

		if(stepsArg != null)
		{
			var v = stepsArg.Substring("--steps=".Length);
			if(int.TryParse(v, out var steps) &&
			   steps > 0)
				BenchParams.Steps = steps;
		}

		// Strip custom args so BenchmarkDotNet does not error on unknown options
		var filteredArgs = args
			.Where(a => !(a.StartsWith("--resource=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--steps=", StringComparison.OrdinalIgnoreCase)))
			.ToArray();

		// Force warmup and iteration counts to 10 for all benchmarks
		var config = DefaultConfig.Instance.AddJob(Job.Default.WithWarmupCount(10).WithIterationCount(10));

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(filteredArgs, config);
	}

	#endregion
}

internal static class BenchParams
{
	#region Interface

	public static string? ResourcePath { get; set; }

	public static int Steps { get; set; } = 1000;

	#endregion
}
