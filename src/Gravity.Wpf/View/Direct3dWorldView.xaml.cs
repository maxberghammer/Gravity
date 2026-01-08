// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;
using MapFlags = Vortice.Direct3D11.MapFlags;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Viewport = Vortice.Mathematics.Viewport;

namespace Gravity.Wpf.View;

/// <summary>
///     Interaction logic for Direct3dWorldView.xaml
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "<Pending>")]
public partial class Direct3dWorldView
{
	#region Fields

	private ID3D11Buffer? _entityBuffer;
	private int _entityCapacity;
	private ID3D11InputLayout? _inputLayout;
	private ID3D11PixelShader? _ps;
	private ID3D11Buffer? _quadBuffer;
	private ID3D11Buffer? _cameraBuffer;
	private ID3D11VertexShader? _vs;
	private ID3D11ShaderResourceView? _entitySrv;
	private ID3D11RasterizerState? _rs;

	#endregion

	#region Construction

	public Direct3dWorldView()
		=> InitializeComponent();

	#endregion

	#region Implementation

	private void CreateShaders(ID3D11Device device)
	{
		const string hlsl = """
							struct EntityGpu
							{
							    float2 Position;
							    float Radius;
							    float StrokeWidth;
							    float3 FillColor;
							    uint Flags;
							    float3 StrokeColor;
							    float _pad;
							};

							StructuredBuffer<EntityGpu> Entities : register(t0);
							
							cbuffer Camera : register(b0)
							{
							    float2 ViewCenter;
							    float ViewScale;
							    float Aspect;
							};
							

							struct VSIn
							{
							    float2 Pos : POSITION;
							    uint InstanceID : SV_InstanceID;
							};

							struct VSOut
							{
							    float4 Pos : SV_POSITION;
							    float2 UV : TEXCOORD;
							    float Radius : RADIUS;
							    float Stroke : STROKE;
							    float3 Fill : FILL;
							    float3 StrokeCol : STROKECOL;
							    uint Flags : FLAGS;
							};

							VSOut VS(VSIn i)
							{
							    EntityGpu e = Entities[i.InstanceID];
							
							    VSOut o;
							
							    float2 world = e.Position + i.Pos * (e.Radius + e.StrokeWidth);
							
							    float2 view = (world - ViewCenter) / ViewScale;
							
							    // Aspect-Korrektur
							    // view.x /= Aspect;
							    
							    // Aspect-Korrektur: X mit (Höhe/Breite) multiplizieren
								view.x *= Aspect;
							
							    o.Pos = float4(view, 0, 1);
							    o.UV = i.Pos;
							    o.Radius = e.Radius;
							    o.Stroke = e.StrokeWidth;
							    o.Fill = e.FillColor;
							    o.StrokeCol = e.StrokeColor;
							    o.Flags = e.Flags;
							
							    return o;
							}
							

							float4 PS(VSOut i) : SV_Target
							{
							    float d = dot(i.UV, i.UV);
								if (d > 1) discard;
								
								float z = sqrt(1 - d);
								float3 n = normalize(float3(i.UV, z));
								float3 light = normalize(float3(0.0, 0.0, 200));
								float diff = saturate(dot(n, light));
								
								float inner = i.Radius / (i.Radius + i.Stroke);
								float3 col = (d > inner * inner) ? i.StrokeCol : i.Fill;
								
								if ((i.Flags & 1) != 0 && d > 0.85)
								    col = float3(1,1,0);
								
								return float4(col * diff, 1);
							}
							""";

		var vsCode = Compiler.Compile(hlsl, "VS", "VS.hlsl", "vs_5_0").Span;
		var psCode = Compiler.Compile(hlsl, "PS", "PS.hlsl", "ps_5_0").Span;

		_vs = device.CreateVertexShader(vsCode);
		_ps = device.CreatePixelShader(psCode);

		_inputLayout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32_Float, 0)], vsCode);
	}

	private void EnsureEntityBuffer(ID3D11Device device, int count)
	{
		if(count <= _entityCapacity)
			return;

		_entityBuffer?.Dispose();
		_entityCapacity = count;
		_entityBuffer = device.CreateBuffer(new BufferDescription((uint)(Marshal.SizeOf<EntityGpu>() * count),
																  BindFlags.ShaderResource,
																  ResourceUsage.Dynamic,
																  CpuAccessFlags.Write,
																  ResourceOptionFlags.BufferStructured,
																  (uint)Marshal.SizeOf<EntityGpu>()));
		_entitySrv = device.CreateShaderResourceView(_entityBuffer!,
													 new ShaderResourceViewDescription
													 {
														 Format = Format.Unknown,
														 ViewDimension = ShaderResourceViewDimension.Buffer,
														 Buffer = new BufferShaderResourceView
																  {
																	  NumElements = (uint)_entityCapacity,
																	  FirstElement = 0
																  }
													 });
	}

	private void _drawingSurface_OnLoadContent(object? sender, DrawingSurfaceEventArgs e)
	{
		_rs = e.Device.CreateRasterizerState(new RasterizerDescription
											 {
												 FillMode = FillMode.Solid,
												 CullMode = CullMode.None, // <<< KRITISCH
												 FrontCounterClockwise = false,
												 DepthClipEnable = true
											 });

		// Quad
		Vector2[] quad = [new(-1, -1), new(1, -1), new(-1, 1), new(-1, 1), new(1, -1), new(1, 1)];

		_quadBuffer = e.Device.CreateBuffer(quad, new BufferDescription((uint)(quad.Length * Marshal.SizeOf<Vector2>()), BindFlags.VertexBuffer));
		_cameraBuffer = e.Device.CreateBuffer([
												  new CameraCB()
												  {
													  ViewCenter = Vector2.Zero,
													  ViewScale = 1.0f,
													  Aspect = 1.0f
												  }
											  ],
											  BindFlags.ConstantBuffer,
											  ResourceUsage.Dynamic,
											  CpuAccessFlags.Write);

		CreateShaders(e.Device);
	}

	private void _drawingSurface_OnUnloadContent(object? sender, DrawingSurfaceEventArgs e)
	{
		_inputLayout?.Dispose();
		_ps?.Dispose();
		_vs?.Dispose();
		_quadBuffer?.Dispose();
		_entityBuffer?.Dispose();
		_entitySrv?.Dispose();
		_rs?.Dispose();
		_cameraBuffer?.Dispose();
	}

	private World Viewmodel
		=> (World)DataContext;

	private static (double WidthDiu, double HeightDiu) GetCurrentMonitorSizeInDiu(Window window)
	{
		// Fenster-TopLeft in Gerätpixel (PointToScreen liefert device pixels)
		var topLeftInPx = window.PointToScreen(new(0, 0));

		// Factory über DXGI-Helper erstellen
		using var factory = DXGI.CreateDXGIFactory1(out IDXGIFactory1? f).Failure
								? null
								: f;

		// Fallback: Primärbildschirm in DIU
		var fallback = (SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

		if (factory is null)
			return fallback;

		for (uint a = 0; a < 10 ; a++)
		{
			using var adapter = factory.EnumAdapters(a);

			if (adapter is null) break;

			for(uint o = 0; o < 10; o++)
			{
				using var output = adapter.EnumOutputs(o);

				if(output is null)
					break;

				var rect = output.Description.DesktopCoordinates;
				
				if(!rect.Contains(topLeftInPx))
					continue;

				var dpi = VisualTreeHelper.GetDpi(window);

				return (rect.Width / dpi.DpiScaleX, rect.Height / dpi.DpiScaleY);
			}
		}

		return fallback;
	}

	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "<Pending>")]
    [SuppressMessage("Minor Code Smell", "S1481:Unused local variables should be removed", Justification = "<Pending>")]
    private void _drawingSurface_OnDraw(object? sender, DrawEventArgs e)
	{
		(var monitorWidthInDiu, var monitorHeightInDiu) = GetCurrentMonitorSizeInDiu(Application.Current!.MainWindow!);

		// === DEMO-DATEN ===
		var entities = Viewmodel.Entities.ToArray();
		
		var count = entities.Length;

		if(1 > count)
			return;

		EnsureEntityBuffer(e.Device, count);

		Span<EntityGpu> data = stackalloc EntityGpu[count];

		//data[0] = new EntityGpu
		//{
		//	Position = new Vector2(0,0),
		//	Radius = 10,
		//	StrokeWidth = 2,
		//	FillColor = new Vector3(0f, 0f, 0f),
		//	StrokeColor = new Vector3(1f, 1f, 1f),
		//	Flags = 0
		//};
		
		var i = 0;
		foreach(var entity in entities)
			data[i++] = new EntityGpu
						{
							Position = new((float)entity.Position.X, (float)entity.Position.Y),
							Radius = (float)entity.r,
							StrokeWidth = (float)entity.StrokeWidth,
							FillColor = new(entity.Fill.R, entity.Fill.G, entity.Fill.B),
							StrokeColor = entity.Stroke.HasValue
											  ? new(entity.Stroke.Value.R, entity.Stroke.Value.G, entity.Stroke.Value.B)
											  : Vector3.Zero,
							Flags = 0
						};

		var visibleArea = new Rect(new(-ActualWidth / 2, -ActualHeight / 2), new Size(ActualWidth, ActualHeight));
		//var sx = visibleArea.Width / monitorWidthInDiu * 1000.0f;
		//var sy = visibleArea.Height / monitorHeightInDiu * 1000.0f;
		//var vs = Viewmodel.Viewport.ScaleFactor * Math.Min(sx,sy);

		// Feste Welt-Skalierung (unabhängig von der Control-Größe)
		var viewScale = (float)Viewmodel.Viewport.ScaleFactor;

		// Aspect als Höhe/Breite (InvAspect), damit im Shader X multipliziert wird
		var invAspect = (float)(visibleArea.Height / visibleArea.Width);

		e.Context.MapConstantBuffer(_entityBuffer!, count, data);
		e.Context.MapConstantBuffer(_cameraBuffer!, new CameraCB
													{
														ViewCenter = Vector2.Zero,
														ViewScale = viewScale, //(float)vs,
														Aspect = invAspect //(float)(visibleArea.Width / visibleArea.Height)
													});

		e.Context.OMSetRenderTargets(e.Surface.ColorTextureView!, e.Surface.DepthStencilView);

		if (e.Surface.DepthStencilView != null)
			e.Context.ClearDepthStencilView(e.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

		e.Context.ClearRenderTargetView(e.Surface.ColorTextureView,
										new Color4(0, 0, 0, 1));


		e.Context.RSSetViewport(new(0, 0, (float)e.Surface.ActualWidth, (float)e.Surface.ActualHeight, -1000, 1000));
		e.Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
		e.Context.IASetInputLayout(_inputLayout);
		e.Context.IASetVertexBuffers(0, [_quadBuffer!], [(uint)Marshal.SizeOf<Vector2>()], [0]);
		e.Context.VSSetShaderResource(0, _entitySrv);
		e.Context.VSSetConstantBuffer(0, _cameraBuffer);
		e.Context.VSSetShader(_vs);
		e.Context.PSSetShader(_ps);
		e.Context.RSSetState(_rs);
		e.Context.DrawInstanced(6, (uint)count, 0, 0);
	}
	
	#endregion
}