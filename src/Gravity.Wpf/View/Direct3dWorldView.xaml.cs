// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Diagnostics.CodeAnalysis;
using Gravity.Wpf.Viewmodel;
using Vortice.Wpf;

namespace Gravity.Wpf.View;

/// <summary>
///     Interaction logic for Direct3dWorldView.xaml
/// </summary>
public partial class Direct3dWorldView
{
	#region Fields

	private VorticeComponent[] _components = [];

	#endregion

	#region Construction

	public Direct3dWorldView()
		=> InitializeComponent();

	#endregion

	#region Implementation

	private World Viewmodel
		=> (World)DataContext;

	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Passiert im Unload")]
	private void OnLoadContent(object? sender, DrawingSurfaceEventArgs e)
	{
		_components = [new Viewport(Viewmodel), new Bodies(Viewmodel), new Paths(Viewmodel)];

		foreach(var component in _components)
			component.Load(e);
	}

	private void OnUnloadContent(object? sender, DrawingSurfaceEventArgs e)
	{
		foreach(var component in _components)
			component.Dispose();
	}

	private void OnDraw(object? sender, DrawEventArgs e)
	{
		foreach(var component in _components)
			component.Draw(e);
	}

	#endregion
}