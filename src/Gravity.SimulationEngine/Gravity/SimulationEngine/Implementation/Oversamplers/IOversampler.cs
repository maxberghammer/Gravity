using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal interface IOversampler
{
	int Oversample(Body[] entitiesToProcess, TimeSpan timeSpan, Action<Body[], TimeSpan> processEntities);
}