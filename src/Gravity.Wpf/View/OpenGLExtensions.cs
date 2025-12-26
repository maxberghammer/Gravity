// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpGL;

// ReSharper disable InconsistentNaming

namespace Gravity.Wpf.View
{
	internal static class OpenGLExtensions
	{
		#region Internal types

		private delegate void glTexImage2DMultisample(uint aTarget, int samples, uint internalformat, int width, int height, bool fixedsamplelocations);

		private delegate void glBlitFramebuffer(uint srcX0, uint srcY0, uint srcX1, uint srcY1, uint dstX0, uint dstY0, uint dstX1, uint dstY1, uint mask,
												uint filter);

		#endregion

		#region Fields

		public const int GL_READ_FRAMEBUFFER = 0x8CA8;
		public const int GL_DRAW_FRAMEBUFFER = 0x8CA9;

		private static readonly Dictionary<string, Delegate> mExtensionFunctions = new();

		#endregion

		#region Interface

		public static void TexImage2DMultisample(this OpenGL gl, uint target, int samples, uint internalformat, int width, int height,
												 bool fixedsamplelocations)
			=> GetDelegateFor<glTexImage2DMultisample>()?.Invoke(target, samples, internalformat, width, height, fixedsamplelocations);

		public static void BlitFramebuffer(this OpenGL gl, uint srcX0, uint srcY0, uint srcX1, uint srcY1, uint dstX0, uint dstY0, uint dstX1, uint dstY1,
										   uint mask, uint filter)
			=> GetDelegateFor<glBlitFramebuffer>()?.Invoke(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

		#endregion

		#region Implementation

		private static T? GetDelegateFor<T>() where T : class
		{
			//  Get the type of the extension function.
			var delegateType = typeof(T);

			//  Get the name of the extension function.
			var name = delegateType.Name;

			if (mExtensionFunctions.TryGetValue(name, out var del))
				return del as T;

			var proc = SharpGL.Win32.wglGetProcAddress(name);

			if (proc == IntPtr.Zero)
				throw new InvalidOperationException("Extension function " + name + " not supported");

			//  Get the delegate for the function pointer.
			del = Marshal.GetDelegateForFunctionPointer(proc, delegateType);

			//  Add to the dictionary.
			mExtensionFunctions.Add(name, del);

			return del as T;
		}

		#endregion
	}
}