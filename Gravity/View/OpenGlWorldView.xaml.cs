using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Gravity.Viewmodel;
using SharpGL;
using SharpGL.Enumerations;
using SharpGL.WPF;

namespace Gravity.View
{
	/// <summary>
	///     Interaction logic for OpenGlWorldView.xaml
	/// </summary>
	public partial class OpenGlWorldView
	{
		private readonly Dictionary<int, List<Vector>> mPathsByEntityId = new Dictionary<int, List<Vector>>();

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
		
		private void RenderPaths()
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
				return;

			var paths = mPathsByEntityId.Values.Where(p => p.Count > 1).ToArray();

			if (!paths.Any())
				return;

			var maxSegments = 10000;

			var gl = mOpenGlControl.OpenGL;

			gl.Color(1.0, 1.0, 1.0);
			gl.PointSize(1.0f);

			foreach (var path in paths)
			{
				if (path.Count > maxSegments)
					path.RemoveRange(0, path.Count - maxSegments);

				gl.Begin(BeginMode.LineStrip);
				foreach (var vector in path)
					gl.Vertex(vector.X, vector.Y, 0.0d);
				gl.End();
			}
		}

		private void OnOpenGlInitialized(object aSender, OpenGLRoutedEventArgs aArgs)
		{
			var gl = aArgs.OpenGL;

			gl.Enable(OpenGL.GL_DEPTH_TEST);
			gl.Enable(OpenGL.GL_LIGHTING);
			gl.Enable(OpenGL.GL_LIGHT0);
			gl.Enable(OpenGL.GL_COLOR_MATERIAL);
			gl.Enable(OpenGL.GL_CULL_FACE);
			
			float[] globalAmbient = {0.5f, 0.5f, 0.5f, 1.0f};
			float[] light0Pos = {0.0f, 0.0f, 200.0f, 0.0f};
			float[] light0Ambient = {0.2f, 0.2f, 0.2f, 1.0f};
			float[] light0Diffuse = {0.3f, 0.3f, 0.3f, 1.0f};
			float[] light0Specular = {0.8f, 0.8f, 0.8f, 1.0f};
			float[] lmodelAmbient = {0.2f, 0.2f, 0.2f, 1.0f};

			//gl.LightModel(OpenGL.GL_LIGHT_MODEL_AMBIENT, lmodelAmbient);
			//gl.LightModel(OpenGL.GL_LIGHT_MODEL_AMBIENT, globalAmbient);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_POSITION, light0Pos);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_AMBIENT, light0Ambient);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_DIFFUSE, light0Diffuse);
			gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_SPECULAR, light0Specular);

			gl.ShadeModel(OpenGL.GL_SMOOTH);

			gl.CullFace(OpenGL.GL_FRONT);
		}

		private void OnResized(object aSender, OpenGLRoutedEventArgs aArgs)
		{
			var gl = mOpenGlControl.OpenGL;

			gl.MatrixMode(MatrixMode.Projection);
			gl.LoadIdentity();
			gl.Ortho(0, ActualWidth, ActualHeight, 0, -1000, 1000);
		}

		private void OnOpenGlDraw(object aSender, OpenGLRoutedEventArgs aArgs)
		{
			using var x = OpenGlMsaa.Use(mOpenGlControl);

			var gl = aArgs.OpenGL;

			gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);

			if (!Viewmodel.Entities.Any())
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

			foreach (var entity in Viewmodel.Entities)
			{
				gl.PushMatrix();
				gl.Translate(entity.Position.X, entity.Position.Y, 0.0d);
				
				// Fill
				gl.CullFace(OpenGL.GL_FRONT);
				gl.Color(entity.Fill.Color.ScR, entity.Fill.Color.ScG, entity.Fill.Color.ScB);
				gl.Sphere(q, entity.r, 32, 32);

				// Selektionsrahmen
				if (ReferenceEquals(entity, Viewmodel.SelectedEntity))
				{
					gl.CullFace(OpenGL.GL_BACK);
					gl.Color(1.0, 1.0, 0.0);
					gl.Sphere(q, (entity.r + entity.StrokeWidth) * 1.1, 32, 32);
				}

				// Stroke
				if (null != entity.Stroke && 0 < entity.StrokeWidth)
				{
					gl.CullFace(OpenGL.GL_BACK);
					gl.Color(entity.Stroke.Color.ScR, entity.Stroke.Color.ScG, entity.Stroke.Color.ScB);
					gl.Sphere(q, entity.r + entity.StrokeWidth, 32, 32);
				}

				gl.PopMatrix();
			}

			gl.DeleteQuadric(q);

			RenderPaths();

			gl.Flush();
		}

		private void OnDataContextChanged(object aSender, DependencyPropertyChangedEventArgs aE)
		{
			if (null == Viewmodel)
				return;

			Viewmodel.Updated += (sender, args) => Dispatcher.Invoke(() => mOpenGlControl.DoRender());
		}

		#endregion
	}
}