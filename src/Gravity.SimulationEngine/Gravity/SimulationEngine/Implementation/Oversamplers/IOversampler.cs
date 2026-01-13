using System;

namespace Gravity.SimulationEngine.Implementation.Oversamplers;

internal interface IOversampler
{
	int Oversample(Entity[] entitiesToProcess, TimeSpan timeSpan, Action<Entity[], TimeSpan> processEntities);
}