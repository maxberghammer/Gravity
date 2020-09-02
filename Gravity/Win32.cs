﻿using System.Runtime.InteropServices;

namespace Gravity
{
	internal static class Win32
	{
		#region Internal types

		[StructLayout(LayoutKind.Sequential)]
		public struct DEVMODE
		{
			#region Fields

			private const int CCHDEVICENAME = 0x20;
			private const int CCHFORMNAME = 0x20;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
			public string dmDeviceName;

			public short dmSpecVersion;
			public short dmDriverVersion;
			public short dmSize;
			public short dmDriverExtra;
			public int dmFields;
			public int dmPositionX;
			public int dmPositionY;
			public int dmDisplayOrientation;
			public int dmDisplayFixedOutput;
			public short dmColor;
			public short dmDuplex;
			public short dmYResolution;
			public short dmTTOption;
			public short dmCollate;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
			public string dmFormName;

			public short dmLogPixels;
			public int dmBitsPerPel;
			public int dmPelsWidth;
			public int dmPelsHeight;
			public int dmDisplayFlags;
			public int dmDisplayFrequency;
			public int dmICMMethod;
			public int dmICMIntent;
			public int dmMediaType;
			public int dmDitherType;
			public int dmReserved1;
			public int dmReserved2;
			public int dmPanningWidth;
			public int dmPanningHeight;

			#endregion
		}

		#endregion

		#region Fields

		private const int ENUM_CURRENT_SETTINGS = -1;
		private const int ENUM_REGISTRY_SETTINGS = -2;

		#endregion

		#region Interface

		[DllImport("user32.dll")]
		public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

		#endregion
	}
}