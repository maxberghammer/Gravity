// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

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

									 // ------------- Pfade (Linien) -------------
									 struct VSIn
									 {
									     float2 Pos : POSITION; // Welt-Koordinate (X,Y)
									 };

									 struct VSOut
									 {
									     float4 Pos : SV_POSITION;
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

									     o.Pos = float4(ndc, 0, 1);
									     return o;
									 }

									 float4 PS(VSOut i) : SV_Target
									 {
									     // Einfach weiß wie in OpenGL-Variante
									     return float4(1, 1, 1, 1);
									 }
									 """;

		private const int _maxPathSegments = 10_000;
		private readonly Dictionary<int, List<Vector2>> _pathsByEntityId = new();
		private ID3D11Buffer? _buffer;
		private ID3D11InputLayout? _inputLayout;
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
				if(!_pathsByEntityId.TryGetValue(entity.Id, out var path1))
					_pathsByEntityId[entity.Id] = path1 = [new((float)entity.Position.X, (float)entity.Position.Y)];

				var last = path1[^1];
				var pos = new Vector2((float)entity.Position.X, (float)entity.Position.Y);

				if(Vector2.Distance(pos, last) >= (float)(1.0 / World.Viewport.ScaleFactor))
					path1.Add(pos);

				// Begrenzen wie in OpenGL
				if(path1.Count > _maxPathSegments)
					path1.RemoveRange(0, path1.Count - _maxPathSegments);
			}

			if(1 > _pathsByEntityId.Count)
				return;

			EnsureBuffers(e.Device);

			// Pipeline für Linien
			e.Context.IASetInputLayout(_inputLayout);
			e.Context.IASetPrimitiveTopology(PrimitiveTopology.LineStrip);
			e.Context.VSSetShader(_vertexShader);
			e.Context.PSSetShader(_pixelShader);

			// Vertexbuffer binden (stride = float2)
			var stride = (uint)Marshal.SizeOf<Vector2>();
			var offset = 0u;
			e.Context.IASetVertexBuffers(0, [_buffer!], [stride], [offset]);

			// Für jeden Pfad den Buffer neu befüllen und zeichnen
			foreach(var path in _pathsByEntityId.Values.Where(path => path.Count >= 2))
			{
				// Map/Unmap mit WriteDiscard – wir zeichnen sofort danach
				e.Context.Map(_buffer, 0, MapMode.WriteDiscard, MapFlags.None, out var box);

				var byteSpan = box.AsSpan(path.Count * Marshal.SizeOf<Vector2>());
				var dst = MemoryMarshal.Cast<byte, Vector2>(byteSpan);

				// Kopieren
				for(var idx = 0; idx < path.Count; idx++)
					dst[idx] = path[idx];

				e.Context.Unmap(_buffer, 0);

				e.Context.Draw((uint)path.Count, 0);
			}
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
		}

		private void EnsureBuffers(ID3D11Device device)
			=> _buffer ??= device.CreateBuffer(new((uint)(_maxPathSegments * Marshal.SizeOf<Vector2>()),
												   BindFlags.VertexBuffer,
												   ResourceUsage.Dynamic,
												   CpuAccessFlags.Write));

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