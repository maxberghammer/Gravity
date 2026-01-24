using System;
using Gravity.Application.Gravity.Application;
using Wellenlib.ComponentModel;

namespace Gravity.Wpf.Viewmodel;

public class World : NotifyPropertyChanged,
					 IMain.IWorld
{
	#region Fields

	private readonly IApplication.IWorld _world;

	#endregion

	#region Construction

	public World(IApplication.IWorld world)
		=> _world = world;

	#endregion

	#region Implementation of IWorld

	/// <inheritdoc/>
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

	/// <inheritdoc/>
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

	/// <inheritdoc/>
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