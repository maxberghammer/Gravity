using System.Numerics;
using System.Windows;
using Gravity.Application.Gravity.Application;
using Gravity.SimulationEngine;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public sealed class Viewport : NotifyPropertyChanged
{
	#region Fields

	private readonly IApplication.IViewport _viewport;

	#endregion

	#region Construction

	public Viewport(IApplication.IViewport viewport)
		=> _viewport = viewport;

	#endregion

	#region Interface

	public required bool Autocenter
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				_viewport.EnableAutocenter();
			else
				_viewport.DisableAutocenter();
		}
	}

	public required double Scale
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_viewport.Scale(value);
		}
	} = double.NaN;

	public double CameraYaw
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_viewport.RotateCamera(value - _viewport.CurrentCameraYaw, 0);
		}
	}

	public double CameraPitch
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_viewport.RotateCamera(0, value - _viewport.CurrentCameraPitch);
		}
	}

	public Vector3D ToWorld(Point viewportPoint)
		=> _viewport.ToWorld(Vector2.Create(viewportPoint));

	public Point ToViewport(Vector3D worldPoint)
		=> Point.Create(_viewport.ToViewport(worldPoint));

	#endregion
}