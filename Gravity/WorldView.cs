using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Gravity.Viewmodel;

//using Vector = Gravity.Viewmodel.Vector;

namespace Gravity
{
	internal class WorldView : FrameworkElement
	{
		#region Fields

		public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register("Background",
																								   typeof(Brush),
																								   typeof(WorldView),
																								   new PropertyMetadata(default(Brush)));

		private readonly Dictionary<int, List<Vector>> mPathsByEntityId = new Dictionary<int, List<Vector>>();

		#endregion

		#region Construction

		public WorldView()
			=> DataContextChanged += OnDataContextChanged;

		#endregion

		#region Interface

		public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

		#endregion

		#region Implementation

		protected override Size MeasureOverride(Size aConstraint)
			=> aConstraint;

		protected override Size ArrangeOverride(Size aArrangeSize)
			=> aArrangeSize;

		protected override void OnRender(DrawingContext aDrawingContext)
		{
			aDrawingContext.DrawDrawing(Render());
		}

		private World Viewmodel
			=> (World)DataContext;

		private void OnDataContextChanged(object aSender, DependencyPropertyChangedEventArgs aE)
		{
			if (null == Viewmodel)
				return;

			Viewmodel.Updated += (sender, args) => Dispatcher.Invoke(InvalidateVisual);
		}

		private Geometry CreatePathGeometry()
		{
			var entityIds = new HashSet<int>(Viewmodel.Entities.Select(e => e.Id));

			foreach (var id in mPathsByEntityId.Keys.ToArray())
				if (!entityIds.Contains(id))
					mPathsByEntityId.Remove(id);

			foreach (var entity in Viewmodel.Entities)
			{
				if (!mPathsByEntityId.TryGetValue(entity.Id, out var path))
					mPathsByEntityId[entity.Id] = path = new List<Vector>(new[] {entity.Position});

				var lastPathPosition = path.Last();

				//if (path.Count > 1)
				//{
				//	var referencePathPosition = path[path.Count - 2];
				//	var dir = (lastPathPosition - referencePathPosition).Unit();
				//	var dist = (entity.Position - lastPathPosition).Length;
				//	var extrapolatedPosition = lastPathPosition + (dir * dist);

				//	if ((extrapolatedPosition - entity.Position).Length >= 0.25d / Viewmodel.Viewport.ScaleFactor)
				//		path.Add(entity.Position);
				//	else
				//		path[path.Count - 1] = extrapolatedPosition;

				//	continue;
				//}

				if ((entity.Position - lastPathPosition).Length >= 1.0d / Viewmodel.Viewport.ScaleFactor)
					path.Add(entity.Position);
			}

			if (!Viewmodel.ShowPath)
				return null;

			var paths = mPathsByEntityId.Values.Where(p => p.Count > 1).ToArray();

			if (!paths.Any())
				return null;

			var geometry = new StreamGeometry();
			var geometryContext = geometry.Open();

			var maxSegments = Math.Min(500, 10000 / paths.Length);

			foreach (var path in paths)
			{
				if (path.Count > maxSegments)
					path.RemoveRange(0, path.Count - maxSegments);

				var lastPos = path.LastOrDefault();

				geometryContext.BeginFigure(new Point(lastPos.X, lastPos.Y), false, false);
				geometryContext.PolyLineTo(((IEnumerable<Vector>)path).Reverse().Skip(1).Select(v => (Point)v).ToArray(), true, true);
			}

			geometryContext.Close();
			geometry.Freeze();

			return geometry;
		}

		private Drawing Render()
		{
			var ret = new DrawingGroup();
			var dc = ret.Open();

			var visibleArea = new Rect(new Point(0, 0), new Size(ActualWidth, ActualHeight));

			dc.DrawRectangle(Background, null, visibleArea);
			dc.PushClip(new RectangleGeometry(visibleArea));

			if (Viewmodel.Entities.Any())
			{
				dc.PushTransform(new ScaleTransform(Viewmodel.Viewport.ScaleFactor, 
													Viewmodel.Viewport.ScaleFactor,
													0, 
													0));
				dc.PushTransform(new TranslateTransform(-Viewmodel.Viewport.TopLeft.X, 
														-Viewmodel.Viewport.TopLeft.Y));

				var path = CreatePathGeometry();

				if (null != path)
				{
					var pathPen = new Pen(Brushes.White, 1 / Viewmodel.Viewport.ScaleFactor);

					pathPen.Freeze();
					dc.DrawGeometry(null, pathPen, path);
				}

				foreach (var entity in Viewmodel.Entities)
				{
					if (ReferenceEquals(entity, Viewmodel.SelectedEntity))
					{
						var selectionPen = new Pen(Brushes.Yellow, 2 / Viewmodel.Viewport.ScaleFactor) {DashStyle = DashStyles.Dash};

						selectionPen.Freeze();

						dc.DrawEllipse(null, selectionPen, new Point(entity.Position.X, entity.Position.Y), entity.r * 1.1, entity.r * 1.1);
					}

					Pen pen = null;

					if (entity.Stroke != null)
					{
						pen = new Pen(entity.Stroke, entity.StrokeWidth);

						pen.Freeze();
					}

					dc.DrawEllipse(entity.Fill, pen, new Point(entity.Position.X, entity.Position.Y), entity.r, entity.r);
				}
			}

			dc.Close();
			ret.Freeze();

			return ret;
		}

		#endregion
	}
}