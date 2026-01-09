// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Numerics;
using Gravity.Wpf.Viewmodel;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Wpf;
using Size = System.Windows.Size;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	private sealed class Viewport : VorticeComponent
	{
		#region Fields

		private const int _msaaSamples = 8;
		private bool _msaaActive;
		private ID3D11Buffer? _cameraBuffer;
		private int _currentHeight;
		private int _currentWidth;

		// MSAA
		private ID3D11Texture2D? _msaaColor;
		private ID3D11Texture2D? _msaaDepth;
		private ID3D11DepthStencilView? _msaaDsv;
		private ID3D11RenderTargetView? _msaaRtv;
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
																	 DepthClipEnable = true,
																	 AntialiasedLineEnable = true, // Linien glätten (Paths)
																	 MultisampleEnable = true // MSAA aktivieren
																 });

		/// <inheritdoc/>
		protected override void OnDraw(DrawEventArgs e)
		{
			EnsureBuffers(e.Device);
			EnsureMsaaTargets(e.Device, (int)e.Surface.ActualWidth, (int)e.Surface.ActualHeight);

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

			// MSAA-Targets setzen und leeren
			if(_msaaRtv is not null &&
			   _msaaDsv is not null)
			{
				e.Context.OMSetRenderTargets(_msaaRtv, _msaaDsv);
				e.Context.RSSetState(_rasterizerState);
				e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, 0, 1));

				e.Context.ClearDepthStencilView(_msaaDsv, DepthStencilClearFlags.Depth, 1.0f, 0);
				e.Context.ClearRenderTargetView(_msaaRtv, new(0, 0, 0, 1));
			}
			else
			{
				// Fallback ohne MSAA
				e.Context.OMSetRenderTargets(e.Surface.ColorTextureView!, e.Surface.DepthStencilView);
				if(e.Surface.DepthStencilView != null)
					e.Context.ClearDepthStencilView(e.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
				e.Context.ClearRenderTargetView(e.Surface.ColorTextureView, new(0, 0, 0, 1));
				e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, 0, 1));
				e.Context.RSSetState(_rasterizerState);
			}

			e.Context.VSSetConstantBuffer(0, _cameraBuffer);
		}

		/// <inheritdoc />
		protected override void OnAfterDraw(DrawEventArgs e)
		{
			if(_msaaActive &&
			   _msaaColor is not null &&
			   e.Surface.ColorTexture is not null)
			{
				// Format 1:1 vom Ziel übernehmen
				var dst = e.Surface.ColorTexture;
				var fmt = dst.Description.Format;

				// KORREKT: Ziel (non‑MSAA) zuerst, Quelle (MSAA) danach
				e.Context.ResolveSubresource(dst, 0, _msaaColor, 0, fmt);
			}
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

			_msaaRtv?.Dispose();
			_msaaRtv = null;
			_msaaColor?.Dispose();
			_msaaColor = null;
			_msaaDsv?.Dispose();
			_msaaDsv = null;
			_msaaDepth?.Dispose();
			_msaaDepth = null;
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

		private void EnsureMsaaTargets(ID3D11Device device, int width, int height)
		{
			if(width == _currentWidth &&
			   height == _currentHeight)
				return;

			_currentWidth = width;
			_currentHeight = height;

			_msaaRtv?.Dispose();
			_msaaRtv = null;
			_msaaColor?.Dispose();
			_msaaColor = null;
			_msaaDsv?.Dispose();
			_msaaDsv = null;
			_msaaDepth?.Dispose();
			_msaaDepth = null;

			// MSAA-Fähigkeit prüfen
			var q = device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, _msaaSamples);
			var samples = q > 0
							  ? _msaaSamples
							  : 1;
			_msaaActive = samples > 1;

			if(!_msaaActive)
			{
				// Kein MSAA möglich → Fallback: direkt in ColorTextureView rendern (kein Resolve)
				return;
			}

			_msaaColor = device.CreateTexture2D(new()
												{
													Width = (uint)Math.Max(1, width),
													Height = (uint)Math.Max(1, height),
													MipLevels = 1,
													ArraySize = 1,
													Format = Format.B8G8R8A8_UNorm,
													SampleDescription = new((uint)samples, 0),
													Usage = ResourceUsage.Default,
													BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
												});
			_msaaRtv = device.CreateRenderTargetView(_msaaColor);

			_msaaDepth = device.CreateTexture2D(new()
												{
													Width = (uint)Math.Max(1, width),
													Height = (uint)Math.Max(1, height),
													MipLevels = 1,
													ArraySize = 1,
													Format = Format.D24_UNorm_S8_UInt,
													SampleDescription = new((uint)samples, 0),
													Usage = ResourceUsage.Default,
													BindFlags = BindFlags.DepthStencil
												});
			_msaaDsv = device.CreateDepthStencilView(_msaaDepth);
		}

		#endregion
	}

	#endregion
}