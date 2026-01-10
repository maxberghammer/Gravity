// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Gravity.Wpf.Viewmodel;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Wpf;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Gravity.Wpf.View;

public partial class Direct3dWorldView
{
	#region Internal types

	private sealed class Paths : VorticeComponent
	{
		#region Internal types

		private sealed class PathBuffer
		{
			#region Fields

			private readonly Vector2[] _buffer;
			private int _start;

			#endregion

			#region Construction

			public PathBuffer(int capacity)
			{
				_buffer = new Vector2[capacity];
				_start = 0;
				Count = 0;
			}

			#endregion

			#region Interface

			public int Count { get; private set; }

			public Vector2 LastOrDefault()
			{
				if(Count == 0)
					return default;

				var idx = (_start + Count - 1) % _buffer.Length;

				return _buffer[idx];
			}

			public void Add(Vector2 v)
			{
				if(Count < _buffer.Length)
				{
					var idx = (_start + Count) % _buffer.Length;
					_buffer[idx] = v;
					Count++;

					return;
				}

				// overwrite oldest (advance start)
				_buffer[_start] = v;
				_start = (_start + 1) % _buffer.Length;
			}

			// Copies the logical sequence into dst span in at most two slices
			public void CopyTo(Span<Vector2> dst)
			{
				var firstLen = Math.Min(Count, _buffer.Length - _start);
				_buffer.AsSpan(_start, firstLen).CopyTo(dst);
				if(Count > firstLen)
					_buffer.AsSpan(0, Count - firstLen).CopyTo(dst[firstLen..]);
			}

			#endregion
		}

		#endregion

		#region Fields

		private const string _hlsl = """
									 cbuffer Camera : register(b0)
									 {
									     float2 TopLeft;     // Welt
									     float2 ScreenSize;  // DIU (ActualWidth, ActualHeight)
									     float  Scale;       // Zoom
									     float  _pad0;
									     float2 _pad1;
									 };

									 // Pfad-Parameter (pro Draw gesetzt)
									 cbuffer PathParams : register(b1)
									 {
									     uint  PathVertexCount;
									     float3 _padParams;
									 };

									 // ------------- Pfade (Linien) -------------
									 struct VSIn
									 {
									     float2 Pos : POSITION;      // Welt-Koordinate (X,Y)
									     uint   Vid : SV_VertexID;   // Laufindex im aktuellen Pfad
									 };

									 struct VSOut
									 {
									     float4 Pos : SV_POSITION;
									     float  T   : TEXCOORD0;     // 0..1 entlang des Pfades
									 };

									 VSOut VS(VSIn i)
									 {
									     VSOut o;

									     // Welt -> Screen
									     float2 screen = (i.Pos - TopLeft) * Scale;

									     // Screen -> NDC
									     float2 ndc;
									     ndc.x = screen.x * (2.0 / ScreenSize.x) - 1.0;
									     ndc.y = 1.0 - screen.y * (2.0 / ScreenSize.y);

									     // Pfad-Interpolation 0..1 (Start->Ende)
									     float denom = max(1.0, (float)(PathVertexCount - 1));
									     o.T = saturate((float)i.Vid / denom);

									     o.Pos = float4(ndc, 0, 1);
									     return o;
									 }

									 float4 PS(VSOut i) : SV_Target
									 {
									     // Verlauf: Start nahezu schwarz, Ende weiß
									     const float3 startCol = float3(0.05, 0.05, 0.05);
									     const float3 endCol   = float3(1.0, 1.0, 1.0);
									     float3 col = lerp(startCol, endCol, i.T);
									     return float4(col, 1);
									 }
									 """;

		private const int _maxPathSegments = 10_000;
		private readonly Dictionary<int, PathBuffer> _pathsByEntityId = new();
		private ID3D11Buffer? _buffer;
		private ID3D11InputLayout? _inputLayout;
		private ID3D11Buffer? _paramsBuffer;
		private ID3D11PixelShader? _pixelShader;
		private ID3D11VertexShader? _vertexShader;

		#endregion

		#region Construction

		public Paths(World world)
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
			if(!World.ShowPath)
				return;

			var entities = World.Entities.ToArray();
			var entityIds = new HashSet<int>(entities.Select(e1 => e1.Id));

			foreach(var id in _pathsByEntityId.Keys.Where(id => !entityIds.Contains(id)).ToArray())
				_pathsByEntityId.Remove(id);

			// Anhängen wenn Bewegungsschwelle überschritten
			foreach(var entity in entities)
			{
				if(!_pathsByEntityId.TryGetValue(entity.Id, out var pathBuf))
					_pathsByEntityId[entity.Id] = pathBuf = new(_maxPathSegments);

				var last = pathBuf.LastOrDefault();
				var pos = new Vector2((float)entity.Position.X, (float)entity.Position.Y);

				if(pathBuf.Count == 0 ||
				   Vector2.Distance(pos, last) >= (float)(1.0 / World.Viewport.ScaleFactor))
					pathBuf.Add(pos);
			}

			if(1 > _pathsByEntityId.Count)
				return;

			EnsureBuffers(e.Device);

			// Pipeline für Linien
			e.Context.IASetInputLayout(_inputLayout);
			e.Context.IASetPrimitiveTopology(PrimitiveTopology.LineStrip);
			e.Context.VSSetShader(_vertexShader);
			e.Context.PSSetShader(_pixelShader);

			// ConstantBuffer b1 für Pfadparameter binden
			e.Context.VSSetConstantBuffer(1, _paramsBuffer);

			// Vertexbuffer binden (stride = float2)
			var stride = (uint)Marshal.SizeOf<Vector2>();
			var offset = 0u;
			e.Context.IASetVertexBuffers(0, [_buffer!], [stride], [offset]);

			// Für jeden Pfad den Buffer neu befüllen und zeichnen
			foreach(var path in _pathsByEntityId.Values)
			{
				if(path.Count < 2)
					continue;

				// Map/Unmap mit WriteDiscard – wir zeichnen sofort danach
				e.Context.Map(_buffer, 0, MapMode.WriteDiscard, MapFlags.None, out var box);

				var byteSpan = box.AsSpan(path.Count * Marshal.SizeOf<Vector2>());
				var dst = MemoryMarshal.Cast<byte, Vector2>(byteSpan);

				// Kopieren (max. 2 Slices bei Wrap)
				path.CopyTo(dst);

				e.Context.Unmap(_buffer, 0);

				// Pfadlänge in b1 setzen (für SV_VertexID-Normalisierung)
				e.Context.MapConstantBuffer(_paramsBuffer!, new PathParams { PathVertexCount = (uint)path.Count });

				// Zeichnen
				e.Context.Draw((uint)path.Count, 0);
			}
		}

		/// <inheritdoc/>
		protected override void OnAfterDraw(DrawEventArgs e)
		{
		}

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			if(!disposing)
				return;

			_inputLayout?.Dispose();
			_inputLayout = null;
			_vertexShader?.Dispose();
			_vertexShader = null;
			_pixelShader?.Dispose();
			_pixelShader = null;
			_buffer?.Dispose();
			_buffer = null;
			_paramsBuffer?.Dispose();
			_paramsBuffer = null;
		}

		private void EnsureBuffers(ID3D11Device device)
		{
			_buffer ??= device.CreateBuffer(new((uint)(_maxPathSegments * Marshal.SizeOf<Vector2>()),
												BindFlags.VertexBuffer,
												ResourceUsage.Dynamic,
												CpuAccessFlags.Write));

			// b1: PathParams (16-Byte aligned)
			_paramsBuffer ??= device.CreateBuffer([new PathParams { PathVertexCount = 0 }],
												  BindFlags.ConstantBuffer,
												  ResourceUsage.Dynamic,
												  CpuAccessFlags.Write);
		}

		private void CreateShaders(ID3D11Device device)
		{
			var vsCode = Compiler.Compile(_hlsl, "VS", "VS.hlsl", "vs_5_0").Span;
			var psCode = Compiler.Compile(_hlsl, "PS", "PS.hlsl", "ps_5_0").Span;

			_vertexShader = device.CreateVertexShader(vsCode);
			_pixelShader = device.CreatePixelShader(psCode);
			_inputLayout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32_Float, 0)], vsCode);
		}

		#endregion
	}

	#endregion
}