using Gravity.Application.Gravity.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gravity.Application.Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{
	#region Internal types

	extension(IServiceCollection services)
	{
		#region Interface

		public IServiceCollection AddGravityApplication()
		{
			services.TryAddSingleton<IApplication, Gravity.Application.Implementation.Application>();

			return services;
		}

		#endregion
	}

	#endregion
}