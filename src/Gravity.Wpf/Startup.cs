using System.Diagnostics.CodeAnalysis;
using Gravity.Application.Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Wellenlib.Windows.Hosting.Wpf;

namespace Gravity.Wpf;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed class Startup : StartupBase<Startup, AppSettings>
{
	#region Construction

	/// <inheritdoc/>
	public Startup(IContext context)
		: base(context, OnAddAppServices)
	{
	}

	#endregion

	#region Implementation

	private static void OnAddAppServices(IServiceCollection services, IContext context)
		=> services.AddGravityApplication();

	#endregion
}