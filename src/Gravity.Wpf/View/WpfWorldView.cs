// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;
using Color = System.Windows.Media.Color;

namespace Gravity.Wpf.View
{
	internal class WpfWorldView : FrameworkElement
	{
		#region Fields

		public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register("Background",
																								   typeof(Brush),
																								   typeof(WpfWorldView),
																								   new(default(Brush)));

		private readonly Dictionary<int, List<Vector2D>> _pathsByEntityId = new();

		#endregion

		#region Construction

		public WpfWorldView()
			=> DataContextChanged += OnDataContextChanged;

		#endregion

		#region Interface

		public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

		#endregion

		#region Implementation

		protected override Size MeasureOverride(Size availableSize)
			=> availableSize;

		protected override Size ArrangeOverride(Size finalSize)
			=> finalSize;

		protected override void OnRender(DrawingContext drawingContext)
			=> drawingContext.DrawDrawing(Render());

		private World Viewmodel
			=> (World)DataContext;

        [SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<Pending>")]
        [SuppressMessage("Usage", "VSTHRD110:Observe result of async calls", Justification = "<Pending>")]
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
		{
			if(null == Viewmodel)
				return;

			Viewmodel.Updated += (_, _) => Dispatcher.InvokeAsync(InvalidateVisual);
		}

        [SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with \"LINQ\" expressions", Justification = "<Pending>")]
        [SuppressMessage("Minor Code Smell", "S6608:Prefer indexing instead of \"Enumerable\" methods on types implementing \"IList\"", Justification = "<Pending>")]
        [SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method", Justification = "<Pending>")]
        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "<Pending>")]
        private Geometry? CreatePathGeometry()
		{
			var entities = Viewmodel.Entities.ToArrayLocked();
			var entityIds = new HashSet<int>(entities.Select(e => e.Id));

			foreach(var id in _pathsByEntityId.Keys.ToArray())
				if(!entityIds.Contains(id))
					_pathsByEntityId.Remove(id);

			foreach(var entity in entities)
			{
				if(!_pathsByEntityId.TryGetValue(entity.Id, out var path))
					_pathsByEntityId[entity.Id] = path = new(new[] { entity.Position });

				var lastPathPosition = path.Last();

				if((entity.Position - lastPathPosition).Length >= 1.0d / Viewmodel.Viewport.ScaleFactor)
					path.Add(entity.Position);
			}

			if(!Viewmodel.ShowPath)
				return null;

			var paths = _pathsByEntityId.Values.Where(p => p.Count > 1).ToArray();

			if(!paths.Any())
				return null;

			var geometry = new StreamGeometry();
			var geometryContext = geometry.Open();

			var maxSegments = Math.Min(500, 10000 / paths.Length);

			foreach(var path in paths)
			{
				if(path.Count > maxSegments)
					path.RemoveRange(0, path.Count - maxSegments);

				var lastPos = path.LastOrDefault();

				geometryContext.BeginFigure(new(lastPos.X, lastPos.Y), false, false);
				geometryContext.PolyLineTo(((IEnumerable<Vector2D>)path).Reverse().Skip(1).Select(v => new Point(v.X, v.Y)).ToArray(), true, true);
			}

			geometryContext.Close();
			geometry.Freeze();

			return geometry;
		}

        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "<Pending>")]
        private Drawing Render()
		{
			var ret = new DrawingGroup();
			var dc = ret.Open();

			var visibleArea = new Rect(new(0, 0), new Size(ActualWidth, ActualHeight));

			dc.DrawRectangle(Background, null, visibleArea);
			dc.PushClip(new RectangleGeometry(visibleArea));

			var entities = Viewmodel.Entities.ToArrayLocked();

			if(entities.Length != 0)
			{
				dc.PushTransform(new ScaleTransform(Viewmodel.Viewport.ScaleFactor,
													Viewmodel.Viewport.ScaleFactor,
													0,
													0));
				dc.PushTransform(new TranslateTransform(-Viewmodel.Viewport.TopLeft.X,
														-Viewmodel.Viewport.TopLeft.Y));

				var path = CreatePathGeometry();

				if(null != path)
				{
					var pathPen = new Pen(Brushes.White, 1 / Viewmodel.Viewport.ScaleFactor);

					pathPen.Freeze();
					dc.DrawGeometry(null, pathPen, path);
				}

				foreach(var entity in entities)
				{
					if(ReferenceEquals(entity, Viewmodel.SelectedEntity))
					{
						var selectionPen = new Pen(Brushes.Yellow, 2 / Viewmodel.Viewport.ScaleFactor) { DashStyle = DashStyles.Dash };

						selectionPen.Freeze();

						dc.DrawEllipse(null, selectionPen, new(entity.Position.X, entity.Position.Y), entity.r * 1.1, entity.r * 1.1);
					}

					Pen? pen = null;

					if(entity.Stroke != null)
					{
						var strokeBrush = new SolidColorBrush(Color.FromArgb(entity.Stroke.Value.A, entity.Stroke.Value.R, entity.Stroke.Value.G, entity.Stroke.Value.B));

						strokeBrush.Freeze();

						pen = new(strokeBrush, entity.StrokeWidth);

						pen.Freeze();
					}

					var fillBrush = new SolidColorBrush(Color.FromArgb(entity.Fill.A, entity.Fill.R, entity.Fill.G, entity.Fill.B));

					fillBrush.Freeze();

					dc.DrawEllipse(fillBrush, pen, new(entity.Position.X, entity.Position.Y), entity.r, entity.r);
				}
			}

			dc.Close();
			ret.Freeze();

			return ret;
		}

		#endregion
	}
}