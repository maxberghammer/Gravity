// Erstellt am: 22.01.2021
// Erstellt von: Max Berghammer

using System;
using System.Diagnostics;

namespace Gravity.Wpf;

#region Internal types

public sealed class FrameDiagnostics
{
	#region Internal types

	public readonly ref struct Measurement : IDisposable
	{
		#region Fields

		private const double _cpuUtilizationAlpha = 0.2; // smoothing factor
		private const double _frameAlpha = 0.2; // smoothing factor
		private readonly TimeSpan _frameStart;
		private readonly ref MeasurementInfo _measurementInfo;
		private readonly TimeSpan _startProcessCpu;
		private readonly Stopwatch _stopwatch;
		private readonly long _totalAllocatedBytes;

		#endregion

		#region Construction

		internal Measurement(Stopwatch stopwatch,
							 ref MeasurementInfo measurementInfo)
		{
			_stopwatch = stopwatch;
			_measurementInfo = ref measurementInfo;
			_frameStart = _stopwatch.Elapsed;
			_startProcessCpu = Process.GetCurrentProcess().TotalProcessorTime;
			_totalAllocatedBytes = GC.GetTotalAllocatedBytes(false);
		}

		#endregion

		#region Implementation of IDisposable

		void IDisposable.Dispose()
		{
			var frameDuration = _stopwatch.Elapsed - _frameStart;
			var cpuElapsed = Process.GetCurrentProcess().TotalProcessorTime - _startProcessCpu;
			var coreCount = Environment.ProcessorCount;
			var cpuUtilizationInPercent = frameDuration.TotalMilliseconds > 0
											  ? Math.Min(100.0, Math.Max(0.0, cpuElapsed.TotalMilliseconds / frameDuration.TotalMilliseconds * (100.0 / coreCount)))
											  : 0.0;

			_measurementInfo = new(cpuUtilizationInPercent,
								   _cpuUtilizationAlpha * cpuUtilizationInPercent + (1.0 - _cpuUtilizationAlpha) * _measurementInfo.CpuUtilizationEmaInPercent,
								   _measurementInfo.FrameCount + 1,
								   frameDuration.TotalMilliseconds,
								   _frameAlpha * frameDuration.TotalMilliseconds + (1.0 - _frameAlpha) * _measurementInfo.FrameDurationEmaInMs,
								   _totalAllocatedBytes == 0
									   ? 0
									   : GC.GetTotalAllocatedBytes(false) - _totalAllocatedBytes);
		}

		#endregion
	}

	public record struct MeasurementInfo(double CpuUtilizationInPercent,
										 double CpuUtilizationEmaInPercent,
										 int FrameCount,
										 double LastFrameDurationInMs,
										 double FrameDurationEmaInMs,
										 long DeltaAllocated);

	#endregion

	#region Fields

	private readonly Stopwatch _stopwatch = new();
	private MeasurementInfo _lastMeasurement;

	#endregion

	#region Construction

	public FrameDiagnostics()
		=> _stopwatch.Start();

	#endregion

	#region Interface

	public MeasurementInfo LastMeasurement
		=> _lastMeasurement;

	public Measurement Measure()
		=> new(_stopwatch, ref _lastMeasurement);

	#endregion
}

#endregion