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
			    float3 Position;      // 3D world position
			    float Radius;
			    float StrokeWidth;
			    float3 FillColor;
			    uint Flags;
			    float3 StrokeColor;
			};

			StructuredBuffer<Body> Bodies : register(t0);

			cbuffer Camera : register(b0)
			{
			    float4x4 ViewProj;     // Combined View * Projection matrix
			    float3 CameraRight;    // For billboard orientation
			    float _pad0;
			    float3 CameraUp;       // For billboard orientation
			    float _pad1;
			    float2 ScreenSize;     // Screen dimensions
			    float  Scale;          // Zoom factor
			    float _pad2;
			};

			struct VSIn
			{
			    float2 Pos : POSITION;    // Quad vertex (-1..1)
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

			    // Billboard: expand quad in camera-aligned plane
			    float size = e.Radius + e.StrokeWidth;
			    float3 worldPos = e.Position 
			                    + CameraRight * (i.Pos.x * size)
			                    + CameraUp * (i.Pos.y * size);

			    // Transform to clip space
			    o.Pos = mul(float4(worldPos, 1.0), ViewProj);
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
			    // Kreismaske
			    float d = dot(i.UV, i.UV);
			    if (d > 1) discard;
			
			    // "Kugel"-Normal aus UV (Z zeigt zur Kamera)
			    float z = sqrt(1 - d);
			    float3 n = normalize(float3(i.UV, z));
			
			    // Vorderlicht (zur Kamera): front = n.z
			    float front = saturate(n.z);
			
			    // Sanfter Verlauf: Ambient + Diffuse (Half-Lambert-ähnlich)
			    const float ambient = 0.15;
			    float diffuse = front * 0.85;
			    float shade = ambient + diffuse; // 0.15..1.0
			
			    // Dezentes Specular-Hotspot in der Mitte
			    float spec = pow(front, 32.0) * 0.15;
			
			    // Innen- vs. Außenfarbe (Stroke)
			    float inner = i.Radius / (i.Radius + i.Stroke);
			    float3 baseCol = (d > inner * inner) ? i.StrokeCol : i.Fill;
			
			    // Selektion übersteuern (wie bisher)
			    if ((i.Flags & 1) != 0 && d > 0.85)
			        baseCol = float3(1,1,0);
			
			    float3 col = baseCol * shade + spec;
			
			    return float4(col, 1);
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
			var bodies = World.GetBodies();

			var count = bodies.Length;

			if(1 > count)
				return;

			EnsureBuffers(e.Device, count);

			Span<BodyGpu> data = stackalloc BodyGpu[count];

			var i = 0;
			foreach(var body in bodies)
				data[i++] = new()
							{
								Position = new((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z),
								Radius = (float)body.r,
								StrokeWidth = (float)body.AtmosphereThickness,
								FillColor = new(body.Color.ScR, body.Color.ScG, body.Color.ScB),
								StrokeColor = body.AtmosphereColor.HasValue
												  ? new(body.AtmosphereColor.Value.ScR, body.AtmosphereColor.Value.ScG, body.AtmosphereColor.Value.ScB)
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

		/// <inheritdoc />
		protected override void OnAfterDraw(DrawEventArgs e)
		{
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
			_shaderResourceView?.Dispose();
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
