// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;
using SharpGL;
using SharpGL.Enumerations;
using SharpGL.WPF;

namespace Gravity.Wpf.View
{
	public partial class OpenGlWorldView
	{
		#region Fields

		private readonly Dictionary<int, List<Vector2D>> _pathsByEntityId = new();

		#endregion

		#region Construction

		public OpenGlWorldView()
		{
			InitializeComponent();

			DataContextChanged += OnDataContextChanged;
			mOpenGlControl.OpenGLInitialized += OnOpenGlInitialized;
			mOpenGlControl.OpenGLDraw += OnOpenGlDraw;
			mOpenGlControl.Resized += OnResized;
		}

		#endregion

		#region Implementation

		private World Viewmodel
			=> (World)DataContext;

        [SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with \"LINQ\" expressions", Justification = "<Pending>")]
        [SuppressMessage("Minor Code Smell", "S6608:Prefer indexing instead of \"Enumerable\" methods on types implementing \"IList\"", Justification = "<Pending>")]
        [SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method", Justification = "<Pending>")]
        private void RenderPaths()
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
				return;

			var paths = _pathsByEntityId.Values.Where(p => p.Count > 1).ToArray();

			if(!paths.Any())
				return;

			var maxSegments = 10000;

			var gl = mOpenGlControl.OpenGL;

			gl.Color(1.0, 1.0, 1.0);
			gl.PointSize(1.0f);

			foreach(var path in paths)
			{
				if(path.Count > maxSegments)
					path.RemoveRange(0, path.Count - maxSegments);

				gl.Begin(BeginMode.LineStrip);
				foreach(var vector in path)
					gl.Vertex(vector.X, vector.Y, 0.0d);
				gl.End();
			}
		}

		private void OnOpenGlInitialized(object sender, OpenGLRoutedEventArgs args)
		{
			var gl = args.OpenGL;

			gl.Enable(OpenGL.GL_DEPTH_TEST);
			gl.Enable(OpenGL.GL_LIGHTING);
			gl.Enable(OpenGL.GL_LIGHT0);
			gl.Enable(OpenGL.GL_COLOR_MATERIAL);
			gl.Enable(OpenGL.GL_CULL_FACE);

			//float[] globalAmbient = {0.5f, 0.5f, 0.5f, 1.0f};
			float[] light0Pos = { 0.0f, 0.0f, 200.0f, 0.0f };
			float[] light0Ambient = { 0.2f, 0.2f, 0.2f, 1.0f };
			float[] light0Diffuse = { 0.3f, 0.3f, 0.3f, 1.0f };
			float[] light0Specular = { 0.8f, 0.8f, 0.8f, 1.0f };
			//float[] lmodelAmbient = {0.2f, 0.2f, 0.2f, 1.0f};

			//gl.LightModel(OpenGL.GL_LIGHT_MODEL_AMBIENT, lmodelAmbient);
			//gl.LightModel(OpenGL.GL_LIGHT_MODEL_AMBIENT, globalAmbient);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_POSITION, light0Pos);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_AMBIENT, light0Ambient);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_DIFFUSE, light0Diffuse);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_SPECULAR, light0Specular);

			gl.ShadeModel(OpenGL.GL_SMOOTH);

			gl.CullFace(OpenGL.GL_FRONT);
		}

		private void OnResized(object sender, OpenGLRoutedEventArgs args)
		{
			var gl = mOpenGlControl.OpenGL;

			gl.MatrixMode(MatrixMode.Projection);
			gl.LoadIdentity();
			gl.Ortho(0, ActualWidth, ActualHeight, 0, -1000, 1000);
		}

		private void OnOpenGlDraw(object sender, OpenGLRoutedEventArgs args)
		{
			using var x = OpenGlMsaa.Use(mOpenGlControl);

			var gl = args.OpenGL;

			gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);

			var entities = Viewmodel.Entities.ToArrayLocked();

			if(entities.Length == 0)
			{
				gl.Flush();

				return;
			}

			gl.MatrixMode(MatrixMode.Modelview);
			gl.LoadIdentity();
			gl.Scale(Viewmodel.Viewport.ScaleFactor, Viewmodel.Viewport.ScaleFactor, Viewmodel.Viewport.ScaleFactor);
			gl.Translate(-Viewmodel.Viewport.TopLeft.X, -Viewmodel.Viewport.TopLeft.Y, 0.0d);

			var q = gl.NewQuadric();
			gl.QuadricNormals(q, OpenGL.GLU_SMOOTH);

			foreach(var entity in entities)
			{
				gl.PushMatrix();
				gl.Translate(entity.Position.X, entity.Position.Y, 0.0d);

				// Fill
				gl.CullFace(OpenGL.GL_FRONT);
				gl.Color(entity.Fill.ScR, entity.Fill.ScG, entity.Fill.ScB);
				gl.Sphere(q, entity.r, 32, 32);

				// Selektionsrahmen
				if(ReferenceEquals(entity, Viewmodel.SelectedEntity))
				{
					gl.CullFace(OpenGL.GL_BACK);
					gl.Color(1.0, 1.0, 0.0);
					gl.Sphere(q, (entity.r + entity.StrokeWidth) * 1.1, 32, 32);
				}

				// Stroke
				if(null != entity.Stroke &&
				   0 < entity.StrokeWidth)
				{
					gl.CullFace(OpenGL.GL_BACK);
					gl.Color(entity.Stroke.Value.ScR, entity.Stroke.Value.ScG, entity.Stroke.Value.ScB);
					gl.Sphere(q, entity.r + entity.StrokeWidth, 32, 32);
				}

				gl.PopMatrix();
			}

			gl.DeleteQuadric(q);

			RenderPaths();

			gl.Flush();
		}

        [SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<Pending>")]
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
		{
			if(null == Viewmodel)
				return;

			Viewmodel.Updated += (_, _) => Dispatcher.Invoke(() => mOpenGlControl.DoRender());
		}

		#endregion
	}
}