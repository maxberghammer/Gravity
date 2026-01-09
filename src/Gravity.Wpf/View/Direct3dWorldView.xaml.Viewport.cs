// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Numerics;
using Gravity.Wpf.Viewmodel;
using Vortice.Direct3D11;
using Vortice.Wpf;
using Size = System.Windows.Size;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	private sealed class Viewport : VorticeComponent
	{
		#region Fields

		private ID3D11Buffer? _cameraBuffer;
		private ID3D11RasterizerState? _rasterizerState;

		#endregion

		#region Construction

		public Viewport(World world)
			: base(world)
		{
		}

		#endregion

		#region Implementation

		/// <inheritdoc/>
		protected override void OnLoad(DrawingSurfaceEventArgs e)
			=> _rasterizerState = e.Device.CreateRasterizerState(new()
																 {
																	 FillMode = FillMode.Solid,
																	 CullMode = CullMode.None, // <<< KRITISCH
																	 FrontCounterClockwise = false,
																	 DepthClipEnable = true
																 });

		/// <inheritdoc/>
		protected override void OnDraw(DrawEventArgs e)
		{
			EnsureBuffers(e.Device);

			// Sichtbarer Bereich in DIU (wie OpenGL gl.Ortho 0..w,0..h)
			var screenSize = new Size(e.Surface.ActualWidth, e.Surface.ActualHeight);

			// Kamera aus dem Viewmodel (wie GL: gl.Scale + gl.Translate)
			var topLeft = new Vector2((float)World.Viewport.TopLeft.X,
									  (float)World.Viewport.TopLeft.Y);
			var scale = (float)World.Viewport.ScaleFactor;

			e.Context.MapConstantBuffer(_cameraBuffer!,
										new CameraGpu
										{
											TopLeft = topLeft, // Welt
											ScreenSize = new((float)screenSize.Width, (float)screenSize.Height), // DIU
											Scale = scale // Zoom
										});

			e.Context.OMSetRenderTargets(e.Surface.ColorTextureView!, e.Surface.DepthStencilView);

			if(e.Surface.DepthStencilView != null)
				e.Context.ClearDepthStencilView(e.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

			e.Context.ClearRenderTargetView(e.Surface.ColorTextureView,
											new(0, 0, 0));

			e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, -1000, 1000));
			e.Context.VSSetConstantBuffer(0, _cameraBuffer);
			e.Context.RSSetState(_rasterizerState);
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			if(!disposing)
				return;

			_rasterizerState?.Dispose();
			_rasterizerState = null;
			_cameraBuffer?.Dispose();
			_cameraBuffer = null;
		}

		private void EnsureBuffers(ID3D11Device device)
			=> _cameraBuffer ??= device.CreateBuffer([
														 new CameraGpu
														 {
															 TopLeft = Vector2.Zero,
															 ScreenSize = new(10f, 10f),
															 Scale = 1.0f
														 }
													 ],
													 BindFlags.ConstantBuffer,
													 ResourceUsage.Dynamic,
													 CpuAccessFlags.Write);

		#endregion
	}

	#endregion
}