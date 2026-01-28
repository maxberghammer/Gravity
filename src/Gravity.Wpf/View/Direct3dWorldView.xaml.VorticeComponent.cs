// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using Gravity.Wpf.Viewmodel;
using Vortice.Wpf;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	private abstract class VorticeComponent : IDisposable
	{
		#region Fields

		private bool _disposedValue;

		#endregion

		#region Construction

		protected VorticeComponent(Viewmodel.Application viewmodel)
			=> Viewmodel = viewmodel;

		#endregion

		#region Interface

		public void Load(DrawingSurfaceEventArgs e)
			=> OnLoad(e);

		public void Draw(DrawEventArgs e)
			=> OnDraw(e);

		public void AfterDraw(DrawEventArgs e)
			=> OnAfterDraw(e);

		#endregion

		#region Implementation of IDisposable

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

		#region Implementation

		protected Viewmodel.Application Viewmodel { get; }

		protected abstract void OnLoad(DrawingSurfaceEventArgs e);

		protected abstract void OnDraw(DrawEventArgs e);

		protected abstract void OnAfterDraw(DrawEventArgs e);

		protected virtual void Dispose(bool disposing)
		{
			if(_disposedValue)
				return;

			if(disposing)
			{
				// TODO: dispose managed state (managed objects)
			}

			// TODO: free unmanaged resources (unmanaged objects) and override finalizer
			// TODO: set large fields to null
			_disposedValue = true;
		}

		#endregion
	}

	#endregion
}