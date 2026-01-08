using System;
using System.Runtime.InteropServices;
using System.Windows;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Gravity.Wpf.View;

internal static class VorticeExtensions
{
	#region Internal types

	extension(IDXGIAdapter adapter)
	{
		#region Interface

		public IDXGIOutput? EnumOutputs(uint output)
			=> adapter.EnumOutputs(output, out var outputOut).Failure
				   ? null
				   : outputOut;

		#endregion
	}

	extension(RawRect rect)
	{
		#region Interface

		public int Width
			=> rect.Right - rect.Left;

		public int Height
			=> rect.Bottom - rect.Top;

		public bool Contains(Point point)
			=> point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;

		#endregion
	}

	extension(IDXGIFactory1 factory)
	{
		#region Interface

		public IDXGIAdapter1? EnumAdapters(uint adapter)
			=> factory.EnumAdapters1(adapter, out var adapterOut).Failure
				   ? null
				   : adapterOut;

		#endregion
	}

	extension(ID3D11DeviceContext1 context)
	{
		#region Interface

		public void MapConstantBuffer<T>(ID3D11Buffer buffer, T data)
			where T : struct
			=> context.MapConstantBuffer(buffer, 1, [data]);

		public void MapConstantBuffer<T>(ID3D11Buffer buffer, int count, Span<T> data)
			where T : struct
		{
			context.Map(buffer, 0, MapMode.WriteDiscard, MapFlags.None, out var box);

			var byteSpan = box.AsSpan(count * Marshal.SizeOf<T>());
			var dst = MemoryMarshal.Cast<byte, T>(byteSpan);

			data.CopyTo(dst);

			context.Unmap(buffer, 0);
		}

		#endregion
	}

	#endregion
}