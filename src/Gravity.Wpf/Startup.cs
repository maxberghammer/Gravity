using System.Diagnostics.CodeAnalysis;
using Wellenlib.Windows.Hosting.Wpf;

namespace Gravity.Wpf;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Wird per DI instanziiert")]
internal sealed class Startup : StartupBase<Startup, AppSettings>
{
	#region Construction

	/// <inheritdoc/>
	public Startup(IContext context)
		: base(context)
	{
	}

	#endregion
}