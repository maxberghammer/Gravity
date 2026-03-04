using System;
using System.Threading.Tasks;
using Wellenlib.Hosting;

namespace Gravity.Wpf;

internal sealed class Program : Wellenlib.Windows.Hosting.Wpf.ProgramBase<Program, AppSettings, Startup, App>,
								IProgram<AppSettings>
{
	#region Implementation

	/// <inheritdoc/>
	static string IProgram<AppSettings>.ApplicationCompany
		=> "Wellental";

	/// <inheritdoc/>
	static string IProgram<AppSettings>.ApplicationName
		=> "Gravity";

	/// <inheritdoc/>
	static Version IProgram<AppSettings>.ApplicationVersion
		=> new(1, 0);

	// ReSharper disable once InconsistentNaming
	private static async Task Main(string[] args)
		=> await RunAsync(args);

	#endregion
}