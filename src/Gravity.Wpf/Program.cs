using System;
using System.Threading.Tasks;
using Wellenlib.Windows.Hosting.Wpf;

namespace Gravity.Wpf;

internal sealed class Program : ProgramBase<Program, AppSettings, Startup, App>
{
	#region Implementation

	/// <inheritdoc/>
	protected override string ApplicationCompany
		=> "Wellental";

	/// <inheritdoc/>
	protected override string ApplicationName
		=> "Wellenlib.Samples.WpfApplication";

	/// <inheritdoc/>
	protected override Version ApplicationVersion
		=> new(1, 0);

	// ReSharper disable once InconsistentNaming
	private static async Task Main(string[] args)
		=> await RunAsync(args);

	#endregion
}