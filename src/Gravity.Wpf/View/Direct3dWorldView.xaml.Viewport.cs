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
		private ID3D11Buffer? _cameraBuffer;
		private int _currentHeight;
		private int _currentWidth;
		private bool _msaaActive;

		// MSAA
		private ID3D11Texture2D? _msaaColor;
		private ID3D11Texture2D? _msaaDepth;
		private ID3D11DepthStencilView? _msaaDsv;
		private ID3D11RenderTargetView? _msaaRtv;
		private ID3D11RasterizerState? _rasterizerState;

		#endregion

		#region Construction

		public Viewport(IMain viewmodel)
			: base(viewmodel)
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

			var screenSize = new Size(e.Surface.ActualWidth, e.Surface.ActualHeight);
			var center = Viewmodel.Application.Viewport.CurrentCenter;

			// 3D Camera setup with orthogonal projection
			var yaw = (float)Viewmodel.Application.Viewport.CurrentCameraYaw;
			var pitch = (float)Viewmodel.Application.Viewport.CurrentCameraPitch;
			var distance = (float)Viewmodel.Application.Viewport.ToWorld((float)Viewmodel.Application.Viewport.CurrentCameraDistance);

			// Camera position: orbit around the center point
			var cosYaw = MathF.Cos(yaw);
			var sinYaw = MathF.Sin(yaw);
			var cosPitch = MathF.Cos(pitch);
			var sinPitch = MathF.Sin(pitch);

			// Camera forward direction (from camera to center)
			// Negated to place camera in front of scene (positive Z), looking back (-Z)
			// This gives right-handed coords: X right, Y up, Z out of screen
			var forward = new Vector3(-sinYaw * cosPitch, -sinPitch, -cosYaw * cosPitch);
			var cameraPos = new Vector3((float)center.X, (float)center.Y, (float)center.Z) - forward * distance;
			var cameraTarget = new Vector3((float)center.X, (float)center.Y, (float)center.Z);

			// World up is Y-axis
			var worldUp = new Vector3(0, 1, 0);

			// Camera right and up vectors (for billboard orientation)
			var cameraRight = Vector3.Normalize(Vector3.Cross(worldUp, forward));
			var cameraUp = Vector3.Normalize(Vector3.Cross(forward, cameraRight));

			// View matrix
			var view = Matrix4x4.CreateLookAt(cameraPos, cameraTarget, worldUp);

			// Orthographic projection
			var orthoWidth = (float)Viewmodel.Application.Viewport.ToWorld((float)screenSize.Width);
			var orthoHeight = (float)Viewmodel.Application.Viewport.ToWorld((float)screenSize.Height);
			var proj = Matrix4x4.CreateOrthographic(orthoWidth, orthoHeight, 0.1f, distance * 10f);

			// Combined ViewProjection matrix (transposed for HLSL column-major)
			var viewProj = Matrix4x4.Transpose(view * proj);

			e.Context.MapConstantBuffer(_cameraBuffer!,
										new CameraGpu
										{
											ViewProj = viewProj,
											CameraRight = cameraRight,
											CameraUp = cameraUp,
											ScreenSize = new((float)screenSize.Width, (float)screenSize.Height),
											Scale = Viewmodel.Application.Viewport.ToViewport(1)
										});

			// MSAA-Targets setzen und leeren
			if(_msaaRtv is not null &&
			   _msaaDsv is not null)
			{
				e.Context.OMSetRenderTargets(_msaaRtv, _msaaDsv);
				e.Context.RSSetState(_rasterizerState);
				e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, 0, 1));

				e.Context.ClearDepthStencilView(_msaaDsv, DepthStencilClearFlags.Depth, 1.0f, 0);
				e.Context.ClearRenderTargetView(_msaaRtv, new(0, 0, 0));
			}
			else
			{
				// Fallback ohne MSAA
				e.Context.OMSetRenderTargets(e.Surface.ColorTextureView!, e.Surface.DepthStencilView);
				if(e.Surface.DepthStencilView != null)
					e.Context.ClearDepthStencilView(e.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
				e.Context.ClearRenderTargetView(e.Surface.ColorTextureView, new(0, 0, 0));
				e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, 0, 1));
				e.Context.RSSetState(_rasterizerState);
			}

			e.Context.VSSetConstantBuffer(0, _cameraBuffer);
		}

		/// <inheritdoc/>
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
															 ViewProj = Matrix4x4.Identity,
															 CameraRight = Vector3.UnitX,
															 CameraUp = Vector3.UnitY,
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
				// Kein MSAA möglich → Fallback: direkt in ColorTextureView rendern (kein Resolve)
				return;

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