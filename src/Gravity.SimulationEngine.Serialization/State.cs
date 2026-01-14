using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Gravity.SimulationEngine.Serialization;

public sealed class State
{
	#region Internal types

	public sealed record Vector(double X, double Y);

	public sealed record ViewportState(Vector TopLeft, Vector BottomRight, double Scale);

	public sealed record BodyState(string FillColor,
								   string? StrokeColor,
								   double StrokeWidth,
								   Vector Position,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   Vector v,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   double r,
								   // ReSharper disable once InconsistentNaming
								   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Das heisst halt in der Physik so")]
								   double m);

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

	public double TimeScale { get; init; }

	public bool ElasticCollisions { get; init; }

	public bool ClosedBoundaries { get; init; }

	public bool ShowPath { get; init; }

	public bool AutoCenterViewport { get; init; }

	public Guid SelectedBodyPresetId { get; init; }

	public Guid? RespawnerId { get; init; }

	public SimpleRng.RngState RngState { get; init; }

	public TimeSpan Runtime { get; init; }

	[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Für die Serialisierung duadses scho")]
	public required BodyState[] Bodies { get; init; }

	public async Task SerializeAsync(StreamWriter swr)
	{
		ArgumentNullException.ThrowIfNull(swr);

		await JsonSerializer.SerializeAsync(swr.BaseStream,
											this,
											_jsonSerializerOptions);
	}

	#endregion
}