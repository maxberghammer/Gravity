// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming
namespace Gravity.Wpf;

[SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "<Pending>")]
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "<Pending>")]
[SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments", Justification = "<Pending>")]
[SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes", Justification = "<Pending>")]
[SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "<Pending>")]
internal static class Win32
{
	#region Internal types

	[StructLayout(LayoutKind.Sequential)]
    public struct DEVMODE
	{
		#region Fields

		public int dmBitsPerPel;
		public short dmCollate;
		public short dmColor;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
		public string dmDeviceName;

		public int dmDisplayFixedOutput;
		public int dmDisplayFlags;
		public int dmDisplayFrequency;
		public int dmDisplayOrientation;
		public int dmDitherType;
		public short dmDriverExtra;
		public short dmDriverVersion;
		public short dmDuplex;
		public int dmFields;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
		public string dmFormName;

		public int dmICMIntent;
		public int dmICMMethod;
		public short dmLogPixels;
		public int dmMediaType;
		public int dmPanningHeight;
		public int dmPanningWidth;
		public int dmPelsHeight;
		public int dmPelsWidth;
		public int dmPositionX;
		public int dmPositionY;
		public int dmReserved1;
		public int dmReserved2;
		public short dmSize;
		public short dmSpecVersion;
		public short dmTTOption;
		public short dmYResolution;
		private const int CCHDEVICENAME = 0x20;
		private const int CCHFORMNAME = 0x20;

		#endregion
	}

    #endregion

    #region Fields

    private const int ENUM_CURRENT_SETTINGS = -1;
	private const int ENUM_REGISTRY_SETTINGS = -2;

	#endregion

	#region Interface

	[DllImport("user32.dll")]
    public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

	#endregion
}