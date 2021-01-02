using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gravity.Viewmodel;
using Microsoft.Win32;

namespace Gravity
{
	public partial class MainWindow
	{
		#region Fields

		private const string mStateFileExtension = "grv";
		private const int mViewportSelectionSearchRadius = 30;

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

			if ((Keyboard.Modifiers == ModifierKeys.None) && (aE.LeftButton == MouseButtonState.Pressed))
			{
				mViewmodel.SelectEntity(viewportPoint, mViewportSelectionSearchRadius);

				if (null != mViewmodel.SelectedEntity)
				{
					var entityViewportPoint = mViewmodel.Viewport.ToViewport(mViewmodel.SelectedEntity.Position);

					mViewmodel.Viewport.DragIndicator = new DragIndicator
														{
															Start = new Vector(entityViewportPoint.X, entityViewportPoint.Y),
															End = new Vector(entityViewportPoint.X, entityViewportPoint.Y)
														};
					return;
				}
			}

			mReferencePosition = mViewmodel.Viewport.ToWorld(viewportPoint);
			mViewmodel.Viewport.DragIndicator = new DragIndicator
												{
													Start = new Vector(viewportPoint.X, viewportPoint.Y),
													End = new Vector(viewportPoint.X, viewportPoint.Y)
												};
		}

		private void OnWorldMouseMove(object aSender, MouseEventArgs aE)
		{
			if (null == mViewmodel.Viewport.DragIndicator)
				return;

			var viewportPoint = aE.GetPosition((IInputElement)aSender);

			mViewmodel.Viewport.DragIndicator.End = new Vector(viewportPoint.X, viewportPoint.Y);

			var position = mViewmodel.Viewport.ToWorld(viewportPoint);

			if (mReferencePosition.HasValue)
			{
				mViewmodel.Viewport.DragIndicator.Label = (aE.RightButton == MouseButtonState.Pressed)
															  ? $"Δv={(position - mReferencePosition.Value) / mViewmodel.TimeScaleFactor}m/s"
															  : $"v={(position - mReferencePosition.Value) / mViewmodel.TimeScaleFactor}m/s";

				return;
			}

			if (null != mViewmodel.SelectedEntity)
				mViewmodel.Viewport.DragIndicator.Label = Keyboard.IsKeyDown(Key.LeftAlt)
															  ? $"Δv={(position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor}m/s"
															  : $"v={(position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor}m/s";
		}

		private void OnWorldMouseLeftButtonUp(object aSender, MouseButtonEventArgs aE)
		{
			var referencePosition = mReferencePosition;
			var position = mViewmodel.Viewport.ToWorld(aE.GetPosition((IInputElement)aSender));

			mViewmodel.Viewport.DragIndicator = null;
			mReferencePosition = null;

			if (null != referencePosition)
			{
				if (Keyboard.IsKeyDown(Key.LeftAlt))
				{
					mViewmodel.CreateRandomEntities(100, Keyboard.IsKeyDown(Key.LeftShift));
					return;
				}

				mViewmodel.CreateEntity(referencePosition.Value, (position - referencePosition.Value) / mViewmodel.TimeScaleFactor);
				mViewmodel.CurrentRespawnerId = null;

				return;
			}

			if (null != mViewmodel.SelectedEntity)
			{
				if ((position - mViewmodel.SelectedEntity.Position).Length <=
					(mViewmodel.SelectedEntity.r + mViewportSelectionSearchRadius / mViewmodel.Viewport.ScaleFactor))
					return;

				if (Keyboard.IsKeyDown(Key.LeftAlt))
					mViewmodel.SelectedEntity.v += (position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor;
				else
					mViewmodel.SelectedEntity.v = (position - mViewmodel.SelectedEntity.Position) / mViewmodel.TimeScaleFactor;
			}
		}

		private void OnWorldRightButtonUp(object aSender, MouseButtonEventArgs aE)
		{
			var referencePosition = mReferencePosition;
			var position = mViewmodel.Viewport.ToWorld(aE.GetPosition((IInputElement)aSender));

			mViewmodel.Viewport.DragIndicator = null;
			mReferencePosition = null;

			if (null != referencePosition)
			{
				if (Keyboard.IsKeyDown(Key.LeftAlt))
				{
					mViewmodel.CreateRandomOrbitEntities(100, Keyboard.IsKeyDown(Key.LeftShift));
					return;
				}

				mViewmodel.CreateOrbitEntity(referencePosition.Value, (position - referencePosition.Value) / mViewmodel.TimeScaleFactor);
				mViewmodel.CurrentRespawnerId = null;
			}
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
										-Math.Sign(aE.Delta) * (Keyboard.IsKeyDown(Key.LeftAlt)
																	? 0.1
																	: 1));

		private void OnAutoScaleAndCenterViewportClicked(object aSender, RoutedEventArgs aE)
			=> mViewmodel.AutoScaleAndCenterViewport();

		private void OnEntityPresetSelectionChanged(object aSender, SelectionChangedEventArgs aE)
		{
			mViewmodel.IsEntityPresetSelectionVisible = false;
		}

		private async void OnSaveClicked(object aSender, RoutedEventArgs aE)
		{
			var dlg = new SaveFileDialog
					  {
						  DefaultExt = mStateFileExtension,
						  Filter = $"Gravity Files | *.{mStateFileExtension}"
					  };
			var dlgResult = dlg.ShowDialog(this);

			if (!dlgResult.Value)
				return;

			await mViewmodel.SaveAsync(dlg.FileName);
		}

		private async void OnOpenClicked(object aSender, RoutedEventArgs aE)
		{
			var dlg = new OpenFileDialog
					  {
						  DefaultExt = mStateFileExtension,
						  Filter = $"Gravity Files | *.{mStateFileExtension}"
					  };
			var dlgResult = dlg.ShowDialog(this);

			if (!dlgResult.Value)
				return;

			await mViewmodel.OpenAsync(dlg.FileName);
		}

		#endregion
	}
}