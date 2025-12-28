// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Gravity.Wpf.View;

/// <summary>
///     Interaction logic for Direct3dWorldView.xaml
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "<Pending>")]
public partial class Direct3dWorldView
{
	#region Fields

	private const int _maxInstances = 100_000;
	private ID3D11Texture2D? _backBuffer;
	private ID3D11DeviceContext? _ctx;
	private D3DImage? _d3dImage;
	private ID3D11Device? _device;
	private int _indexCount;
	private ID3D11InputLayout? _inputLayout;
	private ID3D11Buffer? _instanceVb;
	private ID3D11Buffer? _meshIb;
	private ID3D11Buffer? _meshVb;
	private ID3D11PixelShader? _psFill;
	private ID3D11PixelShader? _psStroke;
	private ID3D11RenderTargetView? _rtv;
	private IDXGISwapChain1? _swapChain;
	private ID3D11VertexShader? _vsFill;
	private ID3D11VertexShader? _vsStroke;
	private ID3D11RasterizerState? _cullBack;
	private ID3D11RasterizerState? _cullFront;

	#endregion

	#region Construction

	public Direct3dWorldView()
		=> InitializeComponent();

	#endregion

	#region Implementation

	// ---------- lifecycle ----------

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		InitializeD3D();
		CompositionTarget.Rendering += OnRender;
	}

	private void OnUnloaded(object sender, RoutedEventArgs e)
	{
		CompositionTarget.Rendering -= OnRender;
		DisposeD3D();
	}

	// ---------- init ----------

	private void InitializeD3D()
	{
		FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

		D3D11.D3D11CreateDevice(null,
								DriverType.Hardware,
								DeviceCreationFlags.BgraSupport,
								levels,
								out _device,
								out _ctx);

		CreateRasterizerStates();
		CreateSwapChain();
		CreateRenderTarget();
		CreateShaders();
		CreateGeometry();
		CreateInstanceBuffer();
		SetupD3DImage();
	}

	private void CreateRasterizerStates()
	{
		_cullBack = _device!.CreateRasterizerState(new RasterizerDescription(CullMode.Back,
																			FillMode.Solid));

		_cullFront = _device!.CreateRasterizerState(new RasterizerDescription(CullMode.Front,
																			 FillMode.Solid));
	}

	private void CreateSwapChain()
	{
		using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
		using var adapter = dxgiDevice.GetAdapter();
		using var factory = adapter.GetParent<IDXGIFactory2>();

		var desc = new SwapChainDescription1
				   {
					   Width = Math.Max(1, (uint)ActualWidth),
					   Height = Math.Max(1, (uint)ActualHeight),
					   Format = Format.B8G8R8A8_UNorm,
					   BufferCount = 2,
					   SampleDescription = new(1, 0),
					   SwapEffect = SwapEffect.FlipSequential,
					   BufferUsage = Usage.RenderTargetOutput
				   };

		_swapChain = factory.CreateSwapChainForComposition(_device!, desc);
	}

	private void CreateRenderTarget()
	{
		_backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
		_rtv = _device!.CreateRenderTargetView(_backBuffer);
	}

	private void SetupD3DImage()
	{
		_d3dImage = new();
		_image.Source = _d3dImage;

		using var surface = _backBuffer!.QueryInterface<IDXGISurface>();

		_d3dImage.Lock();
		_d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9,
								surface.NativePointer);
		_d3dImage.Unlock();
	}

	// ---------- shaders ----------

	private void CreateShaders()
	{
		const string hlsl = """
								 struct VSIn
								 {
								     float3 Pos : POSITION;
								     float3 Nrm : NORMAL;

								     float3 IPos : INSTANCE_POS;
								     float  Rad  : INSTANCE_RADIUS;
								     float4 Fill : FILL_COLOR;
								     float  StrokeW : STROKE_WIDTH;
								     float4 StrokeC : STROKE_COLOR;
								 };

								 struct VSOut
								 {
								     float4 Pos : SV_POSITION;
								     float3 Nrm : NORMAL;
								     float4 Col : COLOR;
								 };

								 cbuffer Camera : register(b0)
								 {
								     float4x4 ViewProj;
								 };

								 VSOut VS_Fill(VSIn i)
								 {
								     VSOut o;
								     float3 wp = i.Pos * i.Rad + i.IPos;
								     o.Pos = mul(float4(wp,1), ViewProj);
								     o.Nrm = i.Nrm;
								     o.Col = i.Fill;
								     return o;
								 }
								 
								 float4 PS_Fill(VSOut i) : SV_Target
								 {
								     float3 L = normalize(float3(0.3,0.5,-1));
								     float d = saturate(dot(i.Nrm, -L));
								     return i.Col * (0.25 + 0.75 * d);
								 }
								 
								 VSOut VS_Stroke(VSIn i)
								 {
								     VSOut o;
								     float r = i.Rad + i.StrokeW;
								     float3 wp = i.Pos * r + i.IPos;
								     o.Pos = mul(float4(wp,1), ViewProj);
								     o.Col = i.StrokeC;
								     return o;
								 }
								 
								 float4 PS_Stroke(VSOut i) : SV_Target
								 {
								     return i.Col;
								 }
								 """;

		var vsFillCode = Compiler.Compile(hlsl, "VS_Fill", "VSFill.hlsl", "vs_5_0").Span;
		var psFillCode = Compiler.Compile(hlsl, "PS_Fill", "PSFill.hlsl", "ps_5_0").Span;
		var vsStrokeCode = Compiler.Compile(hlsl, "VS_Stroke", "VSStroke.hlsl", "vs_5_0").Span;
		var psStrokeCode = Compiler.Compile(hlsl, "PS_Stroke", "PSStroke.hlsl", "ps_5_0").Span;

		_vsFill = _device!.CreateVertexShader(vsFillCode);
		_psFill = _device!.CreatePixelShader(psFillCode);
		_vsStroke = _device!.CreateVertexShader(vsStrokeCode);
		_psStroke = _device!.CreatePixelShader(psStrokeCode);
		
		_inputLayout = _device.CreateInputLayout([
													 // Per-vertex (Slot 0)
													 new("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
													 new("NORMAL", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerVertexData, 0),

													 // Per-instance (Slot 1)
													 new("INSTANCE_POS", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
													 new("INSTANCE_RADIUS", 0, Format.R32_Float, 12, 1, InputClassification.PerInstanceData, 1),
													 new("FILL_COLOR", 0, Format.R32G32B32A32_Float, 16, 1, InputClassification.PerInstanceData, 1),
													 new("STROKE_WIDTH", 0, Format.R32_Float, 32, 1, InputClassification.PerInstanceData, 1),
													 new("STROKE_COLOR", 0, Format.R32G32B32A32_Float, 36, 1, InputClassification.PerInstanceData, 1),
												 ],
												 vsFillCode);
	}

	// ---------- geometry ----------

	private void CreateGeometry()
	{
		// minimal tetrahedron instead of full sphere (placeholder)
		Vertex[] vertices =
		{
			new(new(0, 1, 0), Vector3.UnitY), new(new(-1, -1, 1), Vector3.Normalize(new(-1, -1, 1))), new(new(1, -1, 1), Vector3.Normalize(new(1, -1, 1))),
			new(new(0, -1, -1), Vector3.Normalize(new(0, -1, -1)))
		};

		uint[] indices = { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };

		_indexCount = indices.Length;

		_meshVb = _device!.CreateBuffer(vertices, BindFlags.VertexBuffer);
		_meshIb = _device!.CreateBuffer(indices, BindFlags.IndexBuffer);
	}

	private void CreateInstanceBuffer()
		=> _instanceVb = _device!.CreateBuffer(new((uint)Marshal.SizeOf<InstanceData>() * _maxInstances,
												   BindFlags.VertexBuffer,
												   ResourceUsage.Dynamic,
												   CpuAccessFlags.Write));

	// ---------- render ----------

	private void OnRender(object? sender, EventArgs e)
	{
		if(_ctx is null)
			return;

		_ctx.OMSetRenderTargets(_rtv!);
		_ctx.ClearRenderTargetView(_rtv!, new(0.05f, 0.05f, 0.1f));

		uint[] strides = [(uint)Marshal.SizeOf<Vertex>(), (uint)Marshal.SizeOf<InstanceData>()];
		uint[] offsets = [0, 0];

		_ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
		_ctx.IASetVertexBuffers(0, [_meshVb!, _instanceVb!], strides, offsets);
		_ctx.IASetIndexBuffer(_meshIb!, Format.R32_UInt, 0);
		_ctx.IASetInputLayout(_inputLayout);

		_ctx.RSSetState(_cullFront);
		_ctx.VSSetShader(_vsStroke);
		_ctx.PSSetShader(_psStroke);
		
		_ctx.DrawIndexedInstanced((uint)_indexCount, 1, 0, 0, 0);

		_ctx.RSSetState(_cullBack);
		_ctx.VSSetShader(_vsFill);
		_ctx.PSSetShader(_psFill);
		_ctx.DrawIndexedInstanced((uint)_indexCount, 1, 0, 0, 0);
		
		_swapChain!.Present(1, PresentFlags.None);

		_d3dImage!.Lock();
		_d3dImage.AddDirtyRect(new(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
		_d3dImage.Unlock();
	}

	private void DisposeD3D()
	{
		_rtv?.Dispose();
		_backBuffer?.Dispose();
		_swapChain?.Dispose();
		_ctx?.Dispose();
		_device?.Dispose();
	}

	#endregion
}