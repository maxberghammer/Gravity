using System;
using Gravity.Application.Gravity.Application;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class World : NotifyPropertyChanged
{
	#region Fields

	private readonly IApplication.IWorld _world;

	#endregion

	#region Construction

	public World(IApplication.IWorld world)
		=> _world = world;

	#endregion

	#region Interface

	public required bool ClosedBoundaries
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				_world.EnableClosedBoundaries();
			else
				_world.DisableClosedBoundaries();
		}
	}

	public required bool ElasticCollisions
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			if(value)
				_world.EnableElasticCollisions();
			else
				_world.DisableElasticCollisions();
		}
	}

	public required double LogarithmicTimescale
	{
		get;
		set
		{
			if(!SetProperty(ref field, value))
				return;

			_world.SetTimescale(Math.Pow(10, value));
		}
	} = double.NaN;

	#endregion
}