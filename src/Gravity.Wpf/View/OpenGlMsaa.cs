// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using SharpGL;
using SharpGL.WPF;

namespace Gravity.Wpf.View
{
	internal static class OpenGlMsaa
	{
		#region Internal types

		// ReSharper disable once InconsistentNaming
		private sealed class OpenGLHandles : IDisposable
		{
			#region Fields

			private readonly OpenGL mOpenGl;

			#endregion

			#region Construction

			public OpenGLHandles(OpenGL aOpenGl)
			{
				mOpenGl = aOpenGl;
				var textures = new uint[2];

				aOpenGl.GenTextures(2, textures);
				ColorTexture = textures[0];
				DepthTexture = textures[1];

				var fbos = new uint[1];

				aOpenGl.GenFramebuffersEXT(1, fbos);
				Fbo = fbos[0];
			}

			#endregion

			#region Interface

			public uint ColorTexture { get; }

			public uint DepthTexture { get; }

			public uint Fbo { get; }

			#endregion

			#region Implementation of IDisposable

			/// <inheritdoc />
			public void Dispose()
			{
				mOpenGl.DeleteFramebuffersEXT(1, new[] {Fbo});
				mOpenGl.DeleteTextures(2, new[] {ColorTexture, DepthTexture});
			}

			#endregion
		}

		private sealed class FboAttacher : IDisposable
		{
			#region Fields

			private readonly OpenGLControl mOpenGlControl;

			#endregion

			#region Construction

			public FboAttacher(OpenGLControl aOpenGlControl)
			{
				mOpenGlControl = aOpenGlControl;

				if (GetSamples(mOpenGlControl) < 1)
					return;

				if (!mOpenGlHandlesByOpenGlControl.TryGetValue(aOpenGlControl, out var openGlHandles))
					throw new InvalidOperationException("Please set the attached property OpenGlMSAA.IsEnabled to true on this control!");

				aOpenGlControl.OpenGL.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, openGlHandles.Fbo);
			}

			#endregion

			#region Implementation of IDisposable

			/// <inheritdoc />
			public void Dispose()
			{
				if (GetSamples(mOpenGlControl) < 1)
					return;

				mOpenGlControl.OpenGL.BindFramebufferEXT(OpenGLExtensions.GL_DRAW_FRAMEBUFFER, 0);
				mOpenGlControl.OpenGL.BindFramebufferEXT(OpenGLExtensions.GL_READ_FRAMEBUFFER, mOpenGlHandlesByOpenGlControl[mOpenGlControl].Fbo);
				mOpenGlControl.OpenGL.DrawBuffer(OpenGL.GL_BACK);
				mOpenGlControl.OpenGL.BlitFramebuffer(0,
													  0,
													  (uint)Math.Round(mOpenGlControl.ActualWidth),
													  (uint)Math.Round(mOpenGlControl.ActualHeight),
													  0,
													  0,
													  (uint)Math.Round(mOpenGlControl.ActualWidth),
													  (uint)Math.Round(mOpenGlControl.ActualHeight),
													  OpenGL.GL_DEPTH_BUFFER_BIT | OpenGL.GL_COLOR_BUFFER_BIT,
													  OpenGL.GL_NEAREST);
				mOpenGlControl.OpenGL.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, 0);
			}

			#endregion
		}

		#endregion

		#region Fields

		public static readonly DependencyProperty IsEnabledProperty
			= DependencyProperty.RegisterAttached("IsEnabled",
												  typeof(bool),
												  typeof(OpenGLControl),
												  new FrameworkPropertyMetadata(false,
																				FrameworkPropertyMetadataOptions.AffectsRender));

		public static readonly DependencyProperty SamplesProperty
			= DependencyProperty.RegisterAttached("Samples",
												  typeof(int),
												  typeof(OpenGLControl),
												  new FrameworkPropertyMetadata(8,
																				FrameworkPropertyMetadataOptions.AffectsRender));

		private static readonly Dictionary<OpenGLControl, OpenGLHandles> mOpenGlHandlesByOpenGlControl = new();

		#endregion

		#region Interface

		public static void SetIsEnabled(OpenGLControl aOpenGlControl, bool aValue)
		{
			aOpenGlControl.SetValue(IsEnabledProperty, aValue);

			DisposeMultisampledFbo(aOpenGlControl);

			if (!aValue)
			{
				aOpenGlControl.Resized -= OnOpenGlControlResized;
				return;
			}

			aOpenGlControl.Resized += OnOpenGlControlResized;

			if (aOpenGlControl.ActualWidth > 0 && aOpenGlControl.ActualHeight > 0)
				CreateMultisampledFbo(aOpenGlControl);
		}

		// ReSharper disable once UnusedMember.Global
		public static bool GetIsEnabled(OpenGLControl aOpenGlControl)
			=> (bool)aOpenGlControl.GetValue(IsEnabledProperty);

		public static void SetSamples(OpenGLControl aOpenGlControl, int aValue)
		{
			aOpenGlControl.SetValue(SamplesProperty, aValue);

			DisposeMultisampledFbo(aOpenGlControl);

			if (aOpenGlControl.ActualWidth > 0 && aOpenGlControl.ActualHeight > 0)
				CreateMultisampledFbo(aOpenGlControl);
		}

		public static int GetSamples(OpenGLControl aOpenGlControl)
			=> (int)aOpenGlControl.GetValue(SamplesProperty);

		public static IDisposable Use(OpenGLControl aOpenGlControl)
			=> new FboAttacher(aOpenGlControl);

		#endregion

		#region Implementation

		private static void DisposeMultisampledFbo(OpenGLControl aOpenGlControl)
		{
			if (!mOpenGlHandlesByOpenGlControl.TryGetValue(aOpenGlControl, out var openGlHandles))
				return;

			openGlHandles.Dispose();
			mOpenGlHandlesByOpenGlControl.Remove(aOpenGlControl);
		}

		private static void CreateMultisampledFbo(OpenGLControl aOpenGlControl)
		{
			var samples = GetSamples(aOpenGlControl);

			if (samples < 1)
				return;

			aOpenGlControl.OpenGL.Enable(OpenGL.GL_MULTISAMPLE);

			var openGlHandles = mOpenGlHandlesByOpenGlControl[aOpenGlControl] = new OpenGLHandles(aOpenGlControl.OpenGL);

			aOpenGlControl.OpenGL.BindTexture(OpenGL.GL_TEXTURE_2D_MULTISAMPLE, openGlHandles.ColorTexture);
			aOpenGlControl.OpenGL.TexImage2DMultisample(OpenGL.GL_TEXTURE_2D_MULTISAMPLE,
														samples,
														OpenGL.GL_RGBA,
														(int)Math.Round(aOpenGlControl.ActualWidth),
														(int)Math.Round(aOpenGlControl.ActualHeight),
														false);
			aOpenGlControl.OpenGL.BindTexture(OpenGL.GL_TEXTURE_2D_MULTISAMPLE, openGlHandles.DepthTexture);
			aOpenGlControl.OpenGL.TexImage2DMultisample(OpenGL.GL_TEXTURE_2D_MULTISAMPLE,
														samples,
														OpenGL.GL_DEPTH_COMPONENT,
														(int)Math.Round(aOpenGlControl.ActualWidth),
														(int)Math.Round(aOpenGlControl.ActualHeight),
														false);
			aOpenGlControl.OpenGL.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, openGlHandles.Fbo);
			aOpenGlControl.OpenGL.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT,
														  OpenGL.GL_COLOR_ATTACHMENT0_EXT,
														  OpenGL.GL_TEXTURE_2D_MULTISAMPLE,
														  openGlHandles.ColorTexture,
														  0);
			aOpenGlControl.OpenGL.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT,
														  OpenGL.GL_DEPTH_ATTACHMENT_EXT,
														  OpenGL.GL_TEXTURE_2D_MULTISAMPLE,
														  openGlHandles.DepthTexture,
														  0);

			var status = aOpenGlControl.OpenGL.CheckFramebufferStatusEXT(OpenGL.GL_FRAMEBUFFER_EXT);
			Debug.Assert(OpenGL.GL_FRAMEBUFFER_COMPLETE_EXT == status);

			aOpenGlControl.OpenGL.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, 0);
		}

		private static void OnOpenGlControlResized(object aSender, OpenGLRoutedEventArgs aArgs)
		{
			var sender = (OpenGLControl)aSender;

			DisposeMultisampledFbo(sender);
			CreateMultisampledFbo(sender);
		}

		#endregion
	}
}