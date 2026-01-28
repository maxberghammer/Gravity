// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.ComponentModel;
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

			private readonly Vector3[] _buffer;
			private int _start;

			#endregion

			#region Construction

			public PathBuffer(int capacity)
			{
				_buffer = new Vector3[capacity];
				_start = 0;
				Count = 0;
			}

			#endregion

			#region Interface

			public int Count { get; private set; }

			public Vector3 LastOrDefault()
			{
				if(Count == 0)
					return default;

				var idx = (_start + Count - 1) % _buffer.Length;

				return _buffer[idx];
			}

			public void Add(Vector3 v)
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

			public void Clear()
			{
				_start = 0;
				Count = 0;
			}

			// Copies the logical sequence into dst span in at most two slices
			public void CopyTo(Span<Vector3> dst)
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
									     float4x4 ViewProj;     // Combined View * Projection matrix
									     float3 CameraRight;    // For billboard orientation
									     float _pad0;
									     float3 CameraUp;       // For billboard orientation
									     float _pad1;
									     float2 ScreenSize;     // Screen dimensions
									     float  Scale;          // Zoom factor
									     float _pad2;
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
									     float3 Pos : POSITION;      // 3D Welt-Koordinate
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

									     // Transform to clip space using ViewProj matrix
									     o.Pos = mul(float4(i.Pos, 1.0), ViewProj);

									     // Pfad-Interpolation 0..1 (Start->Ende)
									     float denom = max(1.0, (float)(PathVertexCount - 1));
									     o.T = saturate((float)i.Vid / denom);

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
		private readonly HashSet<int> _bodyIdLookup = new();
		private readonly Dictionary<int, PathBuffer> _pathsByBodyId = new();
		private ID3D11Buffer? _buffer;
		private int _bufferCapacity; // in vertices
		private ID3D11InputLayout? _inputLayout;
		private bool _lastShowPathState;
		private ID3D11Buffer? _paramsBuffer;
		private ID3D11PixelShader? _pixelShader;
		private ID3D11VertexShader? _vertexShader;

		#endregion

		#region Construction

		public Paths(Viewmodel.Application viewmodel)
			: base(viewmodel)
		{
			_lastShowPathState = viewmodel.ShowPath;
			viewmodel.PropertyChanged += OnWorldPropertyChanged;
		}

		#endregion

		#region Implementation

		/// <inheritdoc/>
		protected override void OnLoad(DrawingSurfaceEventArgs e)
			=> CreateShaders(e.Device);

		/// <inheritdoc/>
		protected override void OnDraw(DrawEventArgs e)
		{
			// Check if ShowPath was toggled off and clear paths
			if(_lastShowPathState && !Viewmodel.ShowPath)
				ClearAllPaths();
			_lastShowPathState = Viewmodel.ShowPath;

			if(!Viewmodel.ShowPath)
				return;

			var bodies = Viewmodel.Domain.World.GetBodies();

			// Build HashSet of current body IDs for O(1) lookup
			_bodyIdLookup.Clear();
			for(var i = 0; i < bodies.Count; i++)
				_bodyIdLookup.Add(bodies[i].Id);

			// Remove paths for bodies that no longer exist - now O(m) instead of O(n*m)
			if(_pathsByBodyId.Count > 0)
			{
				var toRemoveCount = 0;
				var toRemove = _pathsByBodyId.Count <= 1024
								   ? stackalloc int[_pathsByBodyId.Count]
								   : new int[_pathsByBodyId.Count];
				foreach(var key in _pathsByBodyId.Keys
												 .Where(key => !_bodyIdLookup.Contains(key)))
					toRemove[toRemoveCount++] = key;

				for(var i = 0; i < toRemoveCount; i++)
					_pathsByBodyId.Remove(toRemove[i]);
			}

			// Append when movement threshold exceeded
			var moveThreshold = (float)Viewmodel.Domain.Viewport.ToWorld(1.0f);

			for(var i = 0; i < bodies.Count; i++)
			{
				var body = bodies[i];
				if(!_pathsByBodyId.TryGetValue(body.Id, out var pathBuf))
					_pathsByBodyId[body.Id] = pathBuf = new(_maxPathSegments);
				var last = pathBuf.LastOrDefault();
				var pos = new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);
				if(pathBuf.Count == 0 ||
				   Vector3.Distance(pos, last) >= moveThreshold)
					pathBuf.Add(pos);
			}

			if(_pathsByBodyId.Count < 1)
				return;

			EnsureBuffers(e.Device);

			// Pipeline for line strips
			e.Context.IASetInputLayout(_inputLayout);
			e.Context.IASetPrimitiveTopology(PrimitiveTopology.LineStrip);
			e.Context.VSSetShader(_vertexShader);
			e.Context.PSSetShader(_pixelShader);

			// ConstantBuffer b1 for path params
			e.Context.VSSetConstantBuffer(1, _paramsBuffer);

			// Bind vertex buffer (stride = float3)
			var stride = (uint)Marshal.SizeOf<Vector3>();
			var offset = 0u;
			e.Context.IASetVertexBuffers(0, [_buffer!], [stride], [offset]);

			// Build compact list of paths to draw
			var valuesArray = _pathsByBodyId.Values.ToArray();
			var pathCount = 0;
			for(var vi = 0; vi < valuesArray.Length; vi++)
				if(valuesArray[vi].Count >= 2)
					pathCount++;

			if(pathCount == 0)
				return;

			var counts = pathCount <= 1024
							 ? stackalloc int[pathCount]
							 : new int[pathCount];
			var bases = pathCount <= 1024
							? stackalloc int[pathCount]
							: new int[pathCount];
			int totalVerts = 0,
				idx = 0;

			for(var vi = 0; vi < valuesArray.Length; vi++)
			{
				var p = valuesArray[vi];

				if(p.Count < 2)
					continue;

				counts[idx] = p.Count;
				bases[idx] = totalVerts;
				totalVerts += p.Count;
				idx++;
			}

			// Map once and copy concatenated
			// Ensure buffer is large enough
			if(totalVerts > _bufferCapacity)
			{
				_eDisposeBuffer();
				_bufferCapacity = Math.Max(totalVerts, _bufferCapacity * 2);
				_buffer = e.Device.CreateBuffer(new((uint)(_bufferCapacity * Marshal.SizeOf<Vector3>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
				// Re-bind vertex buffer with new capacity
				e.Context.IASetVertexBuffers(0, [_buffer!], [stride], [offset]);
			}

			e.Context.Map(_buffer, 0, MapMode.WriteDiscard, MapFlags.None, out var box);
			var byteSpanAll = box.AsSpan(totalVerts * Marshal.SizeOf<Vector3>());
			var dstAll = MemoryMarshal.Cast<byte, Vector3>(byteSpanAll);
			var writeIdx = 0;

			foreach(var pathBuf in _pathsByBodyId.Values)
			{
				if(pathBuf.Count < 2)
					continue;

				pathBuf.CopyTo(dstAll.Slice(bases[writeIdx], counts[writeIdx]));
				writeIdx++;
			}

			e.Context.Unmap(_buffer, 0);

			void _eDisposeBuffer()
			{
				_buffer?.Dispose();
				_buffer = null;
			}

			// Draw each path from its base offset
			for(var i = 0; i < pathCount; i++)
			{
				// Set path length in b1
				e.Context.MapConstantBuffer(_paramsBuffer!, new PathParams { PathVertexCount = (uint)counts[i] });
				// Draw
				e.Context.Draw((uint)counts[i], (uint)bases[i]);
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

			Viewmodel.PropertyChanged -= OnWorldPropertyChanged;

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

		private void OnWorldPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if(e.PropertyName == nameof(Viewmodel.ShowPath) &&
			   !Viewmodel.ShowPath)
				ClearAllPaths();
		}

		private void ClearAllPaths()
		{
			foreach(var pathBuffer in _pathsByBodyId.Values)
				pathBuffer.Clear();
			_pathsByBodyId.Clear();
		}

		private void EnsureBuffers(ID3D11Device device)
		{
			if(_buffer == null)
			{
				_bufferCapacity = _maxPathSegments;
				_buffer = device.CreateBuffer(new((uint)(_bufferCapacity * Marshal.SizeOf<Vector3>()),
												  BindFlags.VertexBuffer,
												  ResourceUsage.Dynamic,
												  CpuAccessFlags.Write));
			}

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
			_inputLayout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32B32_Float, 0)], vsCode);
		}

		#endregion
	}

	#endregion
}