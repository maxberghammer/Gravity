// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Gravity.SimulationEngine;
using Gravity.Wpf.Viewmodel;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using SharpDX.Direct3D11;
using Color = Gravity.SimulationEngine.Color;
using DiffuseMaterial = HelixToolkit.Wpf.SharpDX.DiffuseMaterial;
using Material = HelixToolkit.Wpf.SharpDX.Material;
using MeshGeometry3D = HelixToolkit.SharpDX.Core.MeshGeometry3D;

namespace Gravity.Wpf.View
{
    /// <summary>
    ///     Interaction logic for Direct3dWorldView.xaml
    /// </summary>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "<Pending>")]
    public partial class Direct3dWorldView
	{
		#region Fields

		private readonly ConcurrentDictionary<Color, Material> _allDiffuseMaterialsByColor = new();
		private readonly ConcurrentDictionary<Color, Material> _allPhongMaterialsByColor = new();
		private readonly Dictionary<CullMode, Dictionary<Guid, MeshGeometry3D>> _entityGeometriesByMaterialGuidByCullMode = new();
		private readonly Dictionary<CullMode, Dictionary<Guid, MeshGeometryModel3D>> _entityGeometryModelsByMaterialGuidByCullMode = new();
		private readonly ConcurrentDictionary<int, List<Vector2D>> _pathsByEntityId = new();

		private readonly LineGeometryModel3D _pathsGeometryModel = new()
																   {
																	   Thickness = 0.25,
																	   Color = Colors.LightGray
																   };

        [SuppressMessage("Critical Code Smell", "S4487:Unread \"private\" fields should be removed", Justification = "<Pending>")]
        private readonly List<Element3D> _staticElements;

		private readonly LineGeometryModel3D _worldBoundariesGeometryModel = new()
																			 {
																				 Color = Colors.Lime,
																				 Thickness = 0.1
																			 };

		private int _isUpdating;

		#endregion

		#region Construction

		public Direct3dWorldView()
		{
			InitializeComponent();

			mCamera.LookDirection = new(0, 0, -1);
			_staticElements = mViewport3Dx.Items.ToList();
		}

		#endregion

		#region Implementation

		private static MeshBuilder GetMeshBuilder(Color color, Dictionary<Color, MeshBuilder> allMeshBuildersByColor)
		{
			if(allMeshBuildersByColor.TryGetValue(color, out var ret))
				return ret;

			return allMeshBuildersByColor[color] = new();
		}

		private World Viewmodel
			=> (World)DataContext;

		private void AdjustViewport()
		{
			var zoomFactor = 1 / Viewmodel.Viewport.ScaleFactor * Math.Min(mViewport3Dx.ActualWidth, mViewport3Dx.ActualHeight) / 2.0d; // Warum die 2.0?

			mViewport3Dx.ZoomExtents(new Point3D(Viewmodel.Viewport.Center.X, -Viewmodel.Viewport.Center.Y, 0), zoomFactor);
		}

        [SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<Pending>")]
        private Material GetPhongMaterial(Color color)
		{
			if(_allPhongMaterialsByColor.TryGetValue(color, out var ret))
				return ret;

			lock(_allPhongMaterialsByColor)
			{
				if(_allPhongMaterialsByColor.TryGetValue(color, out ret))
					return ret;

				return _allPhongMaterialsByColor[color] = Dispatcher.Invoke(() => new PhongMaterial
																				  {
																					  AmbientColor = PhongMaterials.Red.AmbientColor,
																					  DiffuseColor = new(color.ScR, color.ScG, color.ScB, color.ScA),
																					  EmissiveColor = PhongMaterials.Red.EmissiveColor,
																					  ReflectiveColor = PhongMaterials.Red.ReflectiveColor,
																					  SpecularColor = PhongMaterials.Red.ReflectiveColor,
																					  SpecularShininess = PhongMaterials.Red.SpecularShininess
																				  });
			}
		}

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "<Pending>")]
        [SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<Pending>")]
        private Material GetDiffuseMaterial(Color color)
		{
			if(_allDiffuseMaterialsByColor.TryGetValue(color, out var ret))
				return ret;

			lock(_allDiffuseMaterialsByColor)
			{
				if(_allDiffuseMaterialsByColor.TryGetValue(color, out ret))
					return ret;

				return _allDiffuseMaterialsByColor[color] = Dispatcher.Invoke(() => new DiffuseMaterial { DiffuseColor = new(color.ScR, color.ScG, color.ScB, color.ScA) });
			}
		}

        [SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with \"LINQ\" expressions", Justification = "<Pending>")]
        [SuppressMessage("Minor Code Smell", "S6608:Prefer indexing instead of \"Enumerable\" methods on types implementing \"IList\"", Justification = "<Pending>")]
        private async Task MaintainPathsAsync(Entity[] entities)
		{
			var entityIds = new HashSet<int>(entities.Select(e => e.Id));

			foreach(var id in _pathsByEntityId.Keys.ToArray())
				if(!entityIds.Contains(id))
					_pathsByEntityId.Remove(id, out var _);

			var minSegmentLength = 1.0d / Viewmodel.Viewport.ScaleFactor;

			await Task.WhenAll(entities.Chunked(World.GetPreferredChunkSize(entities))
									   .Select(chunk => Task.Run(() =>
																 {
																	 foreach(var entity in chunk)
																	 {
																		 if(!_pathsByEntityId.TryGetValue(entity.Id, out var path))
																			 _pathsByEntityId[entity.Id] = path = new(new[] { entity.Position });

																		 var lastPathPosition = path.Last();

																		 if((entity.Position - lastPathPosition).Length >= minSegmentLength)
																			 path.Add(entity.Position);
																	 }
																 })));
		}

		private async Task UpdateItemsAsync(Entity[] entities, Entity? selectedEntity)
		{
			if(1 == Interlocked.CompareExchange(ref _isUpdating, 1, 0))
				return;

			// Pfade pflegen
			await MaintainPathsAsync(entities);

			// Viewport anpassen
			AdjustViewport();

			// Geometrien erzeugen
			_worldBoundariesGeometryModel.Geometry = Viewmodel.ClosedBoundaries
														 ? CreateWorldBoundariesGeometry()
														 : null;
			_pathsGeometryModel.Geometry = Viewmodel.ShowPath
											   ? await Task.Run(CreatePathsGeometry)
											   : null;

			foreach(var g in await Task.Run(() => CreateEntityGeometries(entities, selectedEntity)))
			{
				if(!_entityGeometryModelsByMaterialGuidByCullMode.TryGetValue(g.CullMode, out var entityGeometryModelsByMaterialGuid))
					entityGeometryModelsByMaterialGuid = _entityGeometryModelsByMaterialGuidByCullMode[g.CullMode] = new();

				if(!entityGeometryModelsByMaterialGuid.TryGetValue(g.Material.Core.Guid, out var entityGeometryModel))
					entityGeometryModel = entityGeometryModelsByMaterialGuid[g.Material.Core.Guid] = new()
																									 {
																										 Material = g.Material,
																										 CullMode = g.CullMode
																									 };

				entityGeometryModel.Geometry = g.Geometry;
			}

			// Bei Bedarf Models hinzufügen
			if(!mViewport3Dx.Items.Contains(_worldBoundariesGeometryModel))
				mViewport3Dx.Items.Add(_worldBoundariesGeometryModel);

			if(!mViewport3Dx.Items.Contains(_pathsGeometryModel))
				mViewport3Dx.Items.Add(_pathsGeometryModel);

			foreach(var entityGeometryModel3D in _entityGeometryModelsByMaterialGuidByCullMode.SelectMany(d => d.Value
																												.Select(d2 => d2.Value))
																							  .Where(m => !mViewport3Dx.Items.Contains(m)))
				mViewport3Dx.Items.Add(entityGeometryModel3D);

			_isUpdating = 0;

			// Model updaten
			//mViewport3Dx.Items.Clear();

			//foreach (var element in mStaticElements.Union(dynamicElements))
			//	mViewport3Dx.Items.Add(element);
		}

		private LineGeometry3D CreateWorldBoundariesGeometry()
		{
			var lb = new LineBuilder();

			lb.AddBox(new((float)Viewmodel.Viewport.Center.X, -(float)Viewmodel.Viewport.Center.Y, 0), Viewmodel.Viewport.Size.X,
					  Viewmodel.Viewport.Size.Y, 0);

			return lb.ToLineGeometry3D();
		}

        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "<Pending>")]
        private IEnumerable<(MeshGeometry3D Geometry, Material Material, CullMode CullMode)> CreateEntityGeometries(Entity[] entities, Entity? selectedEntity)
		{
			var ret = new List<(MeshGeometry3D Geometry, Material Material, CullMode CullMode)>();
			var fillMeshBuildersByColor = new Dictionary<Color, MeshBuilder>();
			var strokeMeshBuildersByColor = new Dictionary<Color, MeshBuilder>();
			var selectionMeshBuildersByColor = new Dictionary<Color, MeshBuilder>();

			foreach(var entity in entities)
			{
				// Fill
				GetMeshBuilder(entity.Fill, fillMeshBuildersByColor)
					.AddSphere(new((float)entity.Position.X, (float)-entity.Position.Y, 0),
							   entity.r);

				// Stroke
				if(null != entity.Stroke &&
				   0 < entity.StrokeWidth)
					GetMeshBuilder(entity.Stroke.Value, strokeMeshBuildersByColor)
						.AddSphere(new((float)entity.Position.X, (float)-entity.Position.Y, 0),
								   entity.r + entity.StrokeWidth);

				// Selektionsrahmen
				if(ReferenceEquals(entity, selectedEntity))
					GetMeshBuilder(Color.Yellow, selectionMeshBuildersByColor)
						.AddSphere(new((float)entity.Position.X, (float)-entity.Position.Y, 0),
								   (entity.r + entity.StrokeWidth) * 1.1);
			}

			foreach(var kvp in fillMeshBuildersByColor)
			{
				var cullMode = CullMode.Back;
				var material = GetPhongMaterial(kvp.Key);

				if(!_entityGeometriesByMaterialGuidByCullMode.TryGetValue(cullMode, out var fillEntityGeometriesByMaterialGuid))
					fillEntityGeometriesByMaterialGuid = _entityGeometriesByMaterialGuidByCullMode[cullMode] = new();

				if(!fillEntityGeometriesByMaterialGuid.TryGetValue(material.Core.Guid, out var fillEntityGeometry))
				{
					fillEntityGeometry = fillEntityGeometriesByMaterialGuid[material.Core.Guid] = kvp.Value.ToMeshGeometry3D();
					fillEntityGeometry.IsDynamic = true;
					fillEntityGeometry.PreDefinedIndexCount = 10000000;
					fillEntityGeometry.PreDefinedVertexCount = 10000000;
				}
				else
				{
					fillEntityGeometry.Positions = kvp.Value.Positions;
					fillEntityGeometry.Indices = kvp.Value.TriangleIndices;
				}

				ret.Add((fillEntityGeometry, material, cullMode));
			}

			//ret.AddRange(fillMeshBuildersByColor.Select(kvp => (kvp.Value.ToMeshGeometry3D(),
			//													GetPhongMaterial(kvp.Key),
			//													CullMode.Back)));
			//ret.AddRange(strokeMeshBuildersByColor.Select(kvp => (kvp.Value.ToMeshGeometry3D(),
			//													  GetDiffuseMaterial(kvp.Key),
			//													  CullMode.Front)));
			//ret.AddRange(selectionMeshBuildersByColor.Select(kvp => (kvp.Value.ToMeshGeometry3D(),
			//														 GetDiffuseMaterial(kvp.Key),
			//														 CullMode.Front)));

			return ret;
		}

        [SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method", Justification = "<Pending>")]
        private LineGeometry3D? CreatePathsGeometry()
		{
			var paths = _pathsByEntityId.Values.Where(p => p.Count > 1)
										.ToArray();

			if(!paths.Any())
				return null;

			const int maxSegments = 10000;

			var lb = new LineBuilder();

			foreach(var path in paths)
			{
				if(path.Count > maxSegments)
					path.RemoveRange(0, path.Count - maxSegments);

				lb.Add(false, path.Select(v => new Vector3((float)v.X, -(float)v.Y, 0.0f))
								  .ToArray());
			}

			return lb.ToLineGeometry3D();
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs args)
			=> AdjustViewport();

        [SuppressMessage("Usage", "VSTHRD101:Avoid unsupported async delegates", Justification = "<Pending>")]
        [SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<Pending>")]
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
		{
			if(null == Viewmodel)
				return;

			Viewmodel.Updated +=
				async (_, _) => await Dispatcher.InvokeAsync(async () => await UpdateItemsAsync(Viewmodel.Entities.ToArray(), Viewmodel.SelectedEntity), DispatcherPriority.Render);
		}

        [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
        private async void ButtonBase_OnClick(object sender, RoutedEventArgs args)
		{
			//await UpdateItemsAsync(Viewmodel.Entities.ToArray(), Viewmodel.SelectedEntity);
		}

		#endregion
	}
}