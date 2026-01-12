using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Gravity.SimulationEngine.Implementation;

internal sealed class Diagnostics : ISimulationEngine.IDiagnostics
{
	#region Fields

	private readonly ConcurrentDictionary<string, object> _fields = new();

	#endregion

	#region Interface

	public void SetField(string key, object value)
	{
		if(string.IsNullOrWhiteSpace(key))
			throw new ArgumentException("Key must not be null or whitespace.", nameof(key));

		_fields[key] = value;
	}

	#endregion

	#region Implementation of IDiagnostics

	IReadOnlyDictionary<string, object> ISimulationEngine.IDiagnostics.Fields
		=> _fields;

	#endregion
}