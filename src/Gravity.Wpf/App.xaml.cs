//-------------------------------------------------------------------------------------
// Author:	mbe
// Created:	3/16/2019 8:41:50 PM
// Copyright (c) white duck Gesellschaft für Softwareentwicklung mbH
//-------------------------------------------------------------------------------------

using System;
using System.Windows;
using Microsoft.Extensions.Logging;
using Wellenlib.Hosting;

namespace Gravity.Wpf;

internal sealed partial class App : IApplicationDataProvider
{
	#region Fields

	private readonly IApplicationData _applicationData;
	private readonly ILogger _logger;

	#endregion

	#region Construction

	public App()
		=> throw new NotImplementedException();

	public App(IApplicationData applicationData, ILogger<App> logger)
	{
		_applicationData = applicationData;
		_logger = logger;
	}

	#endregion

	#region Implementation of IApplicationDataProvider

	/// <inheritdoc/>
	IApplicationData IApplicationDataProvider.ApplicationData
		=> _applicationData;

	#endregion

	#region Implementation

	/// <inheritdoc/>
	protected override void OnStartup(StartupEventArgs e)
	{
		_logger.LogDebug("Startup!");

		base.OnStartup(e);
	}

	/// <inheritdoc/>
	protected override void OnExit(ExitEventArgs e)
	{
		base.OnExit(e);

		_logger.LogDebug("Exit!");
	}

	#endregion
}