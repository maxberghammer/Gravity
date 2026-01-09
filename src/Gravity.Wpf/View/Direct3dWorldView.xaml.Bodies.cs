// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Gravity.Wpf.Viewmodel;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Wpf;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	private sealed class Bodies : VorticeComponent
	{
		#region Fields

		private const string _hlsl = """
									 struct Body
									 {
									     float2 Position;
									     float Radius;
									     float StrokeWidth;
									     float3 FillColor;
									     uint Flags;
									     float3 StrokeColor;
									     float _pad;
									 };

									 StructuredBuffer<Body> Bodies : register(t0);

									 cbuffer Camera : register(b0)
									 {
									     float2 TopLeft;     // Welt
									     float2 ScreenSize;  // DIU (ActualWidth, ActualHeight)
									     float  Scale;       // Zoom
									     float  _pad0;
									     float2 _pad1;
									 };

									 struct VSIn
									 {
									     float2 Pos : POSITION;
									     uint   InstanceID : SV_InstanceID;
									 };

									 struct VSOut
									 {
									     float4 Pos : SV_POSITION;
									     float2 UV  : TEXCOORD;
									     float  Radius : RADIUS;
									     float  Stroke : STROKE;
									     float3 Fill   : FILL;
									     float3 StrokeCol : STROKECOL;
									     uint   Flags  : FLAGS;
									 };

									 VSOut VS(VSIn i)
									 {
									     Body e = Bodies[i.InstanceID];

									     VSOut o;

									     // Weltposition des Pixels (Kreis-Quad)
									     float2 world = e.Position + i.Pos * (e.Radius + e.StrokeWidth);

									     // Welt -> Screen (oben links ist (0,0))
									     float2 screen = (world - TopLeft) * Scale;

									     // Screen -> NDC (Ortho wie in OpenGL: gl.Ortho(0,w,h,0,...))
									     float2 ndc;
									     ndc.x = screen.x * (2.0 / ScreenSize.x) - 1.0;
									     ndc.y = 1.0 - screen.y * (2.0 / ScreenSize.y);

									     o.Pos = float4(ndc, 0, 1);
									     o.UV  = i.Pos;
									     o.Radius = e.Radius;
									     o.Stroke = e.StrokeWidth;
									     o.Fill   = e.FillColor;
									     o.StrokeCol = e.StrokeColor;
									     o.Flags  = e.Flags;

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

		private static readonly Vector2[] _quad = [new(-1, -1), new(1, -1), new(-1, 1), new(-1, 1), new(1, -1), new(1, 1)];
		private ID3D11Buffer? _bodyBuffer;
		private int _bodyBufferCapacity;
		private ID3D11InputLayout? _inputLayout;
		private ID3D11PixelShader? _pixelShader;
		private ID3D11Buffer? _quadBuffer;
		private ID3D11ShaderResourceView? _shaderResourceView;
		private ID3D11VertexShader? _vertexShader;

		#endregion

		#region Construction

		public Bodies(World world)
			: base(world)
		{
		}

		#endregion

		#region Implementation

		/// <inheritdoc/>
		protected override void OnLoad(DrawingSurfaceEventArgs e)
			=> CreateShaders(e.Device);

		/// <inheritdoc/>
		protected override void OnDraw(DrawEventArgs e)
		{
			var entities = World.Entities.ToArray();

			var count = entities.Length;

			if(1 > count)
				return;

			EnsureBuffers(e.Device, count);

			Span<BodyGpu> data = stackalloc BodyGpu[count];

			//data[0] = new BodyGpu
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
				data[i++] = new()
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

			e.Context.MapConstantBuffer(_bodyBuffer!, count, data);
			e.Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
			e.Context.IASetInputLayout(_inputLayout);
			e.Context.IASetVertexBuffers(0, [_quadBuffer!], [(uint)Marshal.SizeOf<Vector2>()], [0]);
			e.Context.VSSetShaderResource(0, _shaderResourceView);
			e.Context.VSSetShader(_vertexShader);
			e.Context.PSSetShader(_pixelShader);
			e.Context.DrawInstanced(6, (uint)count, 0, 0);
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			if(!disposing)
				return;

			_quadBuffer?.Dispose();
			_quadBuffer = null;
			_inputLayout?.Dispose();
			_inputLayout = null;
			_pixelShader?.Dispose();
			_pixelShader = null;
			_vertexShader?.Dispose();
			_vertexShader = null;
			_bodyBuffer?.Dispose();
			_bodyBuffer = null;
			_shaderResourceView?.Dispose();
			_shaderResourceView = null;
		}

		private void CreateShaders(ID3D11Device device)
		{
			var vsCode = Compiler.Compile(_hlsl, "VS", "VS.hlsl", "vs_5_0").Span;
			var psCode = Compiler.Compile(_hlsl, "PS", "PS.hlsl", "ps_5_0").Span;

			_vertexShader = device.CreateVertexShader(vsCode);
			_pixelShader = device.CreatePixelShader(psCode);
			_inputLayout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32_Float, 0)], vsCode);
		}

		private void EnsureBuffers(ID3D11Device device, int count)
		{
			_quadBuffer ??= device.CreateBuffer(_quad, new BufferDescription((uint)(_quad.Length * Marshal.SizeOf<Vector2>()), BindFlags.VertexBuffer));

			if(count <= _bodyBufferCapacity)
				return;

			_bodyBuffer?.Dispose();
			_bodyBufferCapacity = count;
			_bodyBuffer = device.CreateBuffer(new((uint)(Marshal.SizeOf<BodyGpu>() * count),
												  BindFlags.ShaderResource,
												  ResourceUsage.Dynamic,
												  CpuAccessFlags.Write,
												  ResourceOptionFlags.BufferStructured,
												  (uint)Marshal.SizeOf<BodyGpu>()));
			_shaderResourceView = device.CreateShaderResourceView(_bodyBuffer!,
																  new ShaderResourceViewDescription
																  {
																	  Format = Format.Unknown,
																	  ViewDimension = ShaderResourceViewDimension.Buffer,
																	  Buffer = new()
																			   {
																				   NumElements = (uint)_bodyBufferCapacity,
																				   FirstElement = 0
																			   }
																  });
		}

		#endregion
	}

	#endregion
}