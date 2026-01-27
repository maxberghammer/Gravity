using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Serialization;

public sealed class State
{
	#region Internal types

	public sealed record Vector(double X, double Y, double Z = 0);

	public sealed record ViewportState(Vector TopLeft, Vector BottomRight, double Scale, bool Autocenter, double CameraYaw = 0, double CameraPitch = 0);

	[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Für die Serialisierung duadses scho")]
	public sealed record WorldState(bool ElasticCollisions, bool ClosedBoundaries, double Timescale, BodyState[] Bodies);

	public sealed record BodyState(int Id,
								   string Color,
								   string? AtmosphereColor,
								   double AtmosphereThickness,
								   Vector Position,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   Vector v,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   double r,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   double m,
								   string? Name);

	#endregion

	#region Fields

	private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

	#endregion

	#region Interface

	public static async Task<State> DeserializeAsync(StreamReader srdr)
	{
		ArgumentNullException.ThrowIfNull(srdr);

		return await JsonSerializer.DeserializeAsync<State>(srdr.BaseStream, _jsonSerializerOptions)
			   ?? throw new InvalidOperationException("Deserialization resulted in null.");
	}

	public required ViewportState Viewport { get; init; }

	public required WorldState World { get; init; }

	public bool ShowPath { get; set; }

	public Guid SelectedBodyPresetId { get; init; }

	public Guid? RespawnerId { get; init; }

	public SimpleRng.RngState RngState { get; init; }

	public TimeSpan Runtime { get; init; }

	public async Task SerializeAsync(StreamWriter swr)
	{
		ArgumentNullException.ThrowIfNull(swr);

		await JsonSerializer.SerializeAsync(swr.BaseStream,
											this,
											_jsonSerializerOptions);
	}

	#endregion
}