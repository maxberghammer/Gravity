using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Gravity.Viewmodel;

namespace Gravity
{
	public partial class MainWindow
	{
		#region Fields

		private static Vector? mReferencePosition;

		#endregion

		#region Construction

		public MainWindow()
		{
			InitializeComponent();
		}

		#endregion

		#region Implementation

		private void OnWorldMouseDown(object aSender, MouseButtonEventArgs aE)
		{
			var viewportPoint = aE.GetPosition((IInputElement)aSender);

			if ((Keyboard.Modifiers == ModifierKeys.None)&&(aE.LeftButton == MouseButtonState.Pressed))
			{
				mViewmodel.SelectEntity(viewportPoint, 30);

				if (null != mViewmodel.SelectedEntity)
					return;
			}

			mReferencePosition = mViewmodel.Viewport.ToWorld(viewportPoint);
		}

		private void OnWorldMouseLeftButtonUp(object aSender, MouseButtonEventArgs aE)
		{
			if (null == mReferencePosition)
				return;

			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				var rnd = new Random();

				var viewportSize = mViewmodel.Viewport.BottomRight - mViewmodel.Viewport.TopLeft;

				for (var i = 0; i < 100; i++)
					mViewmodel.CreateEntity(new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + mViewmodel.Viewport.TopLeft,
											VectorExtensions.Zero);

				mViewmodel.RebuildAbsorbed = Keyboard.IsKeyDown(Key.LeftShift)
												 ? (Action)(() => mViewmodel
																.CreateEntity(new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + mViewmodel.Viewport.TopLeft,
																			  VectorExtensions.Zero))
												 : null;
			}
			else
			{
				var position = mViewmodel.Viewport.ToWorld(aE.GetPosition((IInputElement)aSender));

				mViewmodel.CreateEntity(position, (position - mReferencePosition.Value) / 10);

				mViewmodel.RebuildAbsorbed = null;
			}

			mReferencePosition = null;
		}

		private void OnWorldRightButtonUp(object aSender, MouseButtonEventArgs aE)
		{
			if (null == mReferencePosition)
				return;

			if (Keyboard.IsKeyDown(Key.LeftAlt))
			{
				var rnd = new Random();

				var viewportSize = mViewmodel.Viewport.BottomRight - mViewmodel.Viewport.TopLeft;

				for (var i = 0; i < 100; i++)
					mViewmodel.CreateOrbitEntity(new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + mViewmodel.Viewport.TopLeft,
												 VectorExtensions.Zero);

				mViewmodel.RebuildAbsorbed = Keyboard.IsKeyDown(Key.LeftShift)
												 ? (Action)(() => mViewmodel
																.CreateOrbitEntity(new Vector(rnd.NextDouble() * viewportSize.X, rnd.NextDouble() * viewportSize.Y) + mViewmodel.Viewport.TopLeft,
																				   VectorExtensions.Zero))
												 : null;
			}
			else
			{
				var position = mViewmodel.Viewport.ToWorld(aE.GetPosition((IInputElement)aSender));

				mViewmodel.CreateOrbitEntity(mReferencePosition.Value, (position - mReferencePosition.Value) / 100 * mViewmodel.Viewport.ScaleFactor);

				mViewmodel.RebuildAbsorbed = null;
			}

			mReferencePosition = null;
		}
		
		private void OnWorldSizeChanged(object aSender, SizeChangedEventArgs aE)
		{
			var center = mViewmodel.Viewport.Center;
			var newSize = new Vector(aE.NewSize.Width, aE.NewSize.Height);

			mViewmodel.Viewport.TopLeft = center - newSize / 2 / mViewmodel.Viewport.ScaleFactor;
			mViewmodel.Viewport.BottomRight = center + newSize / 2 / mViewmodel.Viewport.ScaleFactor;
		}

		private void OnResetClicked(object aSender, RoutedEventArgs aE)
			=> mViewmodel.Reset();

		private void OnWorldMouseWheel(object aSender, MouseWheelEventArgs aE)
			=> mViewmodel.Viewport.Zoom(mViewmodel.Viewport.ToWorld(aE.GetPosition((IInputElement)aSender)),
										-Math.Sign(aE.Delta) * (Keyboard.IsKeyDown(Key.LeftCtrl)
																	? 1
																	: 0.1));

		private void OnAutoScaleAndCenterViewportClicked(object aSender, RoutedEventArgs aE)
			=> mViewmodel.AutoScaleAndCenterViewport();

		#endregion
	}
}