using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Gravity.SimulationEngine.Serialization;

namespace Gravity.SimulationEngine.Mock;

public static class IWorldExtensions
{
	#region Internal types

	extension(IWorld world)
	{
		#region Interface

		public static async Task<(IWorld World, TimeSpan DeltaTime)> CreateFromJsonAsync(string json)
		{
			var bytes = Encoding.UTF8.GetBytes(json);
			using var ms = new MemoryStream(bytes);
			using var reader = new StreamReader(ms, Encoding.UTF8, false);
			var state = await State.DeserializeAsync(reader);
			var vp = new ViewportMock(new(state.Viewport.TopLeft.X, state.Viewport.TopLeft.Y, state.Viewport.TopLeft.Z), new(state.Viewport.BottomRight.X, state.Viewport.BottomRight.Y, state.Viewport.BottomRight.Z));
			var bodies = new Body[state.Bodies.Length];

		for(var i = 0; i < bodies.Length; i++)
			{
				var bodyState = state.Bodies[i];
				bodies[i] = new(new(bodyState.Position.X, bodyState.Position.Y, bodyState.Position.Z),
								  bodyState.r,
								  bodyState.m,
								  new(bodyState.v.X, bodyState.v.Y, bodyState.v.Z),
								  Vector3D.Zero,
								  string.IsNullOrEmpty(bodyState.Color)
									  ? Color.Transparent
									  : Color.Parse(bodyState.Color),
								  string.IsNullOrEmpty(bodyState.AtmosphereColor)
									  ? null
									  : Color.Parse(bodyState.AtmosphereColor),
								  bodyState.AtmosphereThickness);
			}

			return (new WorldMock(vp, state.ClosedBoundaries, state.ElasticCollisions, bodies), TimeSpan.FromSeconds(1.0d / 60.0d * Math.Pow(10, state.TimeScale)));
		}

		public static async Task<(IWorld World, TimeSpan DeltaTime)> CreateFromJsonResourceAsync(string jsonResourcePath, Assembly? resourceAssembly = null)
		{
			if(string.IsNullOrWhiteSpace(jsonResourcePath))
				throw new ArgumentNullException(nameof(jsonResourcePath));

			var asm = resourceAssembly ?? typeof(WorldMock).Assembly;
			var normalized = jsonResourcePath.Replace('\\', '/').Replace('/', '.');
			var resourceName = asm.GetManifestResourceNames()
								  .FirstOrDefault(n => n.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));

			if(string.IsNullOrEmpty(resourceName))
				throw new FileNotFoundException($"Resource '{jsonResourcePath}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");

			await using var stream = asm.GetManifestResourceStream(resourceName)
									 ?? throw new FileNotFoundException($"Resource stream '{resourceName}' not found.");
			using var reader = new StreamReader(stream, Encoding.UTF8, true);
			var json = await reader.ReadToEndAsync();

			return await IWorld.CreateFromJsonAsync(json);
		}

		public IWorld CreateMock()
			=> new WorldMock(world.Viewport, world.ClosedBoundaries, world.ElasticCollisions, CloneBodies(world.GetBodies()));

		#endregion
	}

	#endregion

	#region Implementation

	private static Body[] CloneBodies(Body[] baseline)
	{
		var copy = new Body[baseline.Length];

		for(var i = 0; i < baseline.Length; i++)
			copy[i] = baseline[i].Clone();

		return copy;
	}

	#endregion
}