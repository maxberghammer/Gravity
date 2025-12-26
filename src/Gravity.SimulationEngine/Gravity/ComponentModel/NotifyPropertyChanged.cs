//-------------------------------------------------------------------------------------
// Author:      mbe
// Created:     7/31/2013 9:51:53 PM
// Copyright (c) white duck Gesellschaft für Softwareentwicklung mbH
//-------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Gravity.ComponentModel;

public class NotifyPropertyChanged : INotifyPropertyChanged
{
	#region Interface

	public bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, ref bool aChangedFlag, [CallerMemberName] string? aPropertyName = null)
	{
		// ReSharper disable once ExplicitCallerInfoArgument
		if(!SetProperty(ref aStorage, aValue, aPropertyName))
			return false;

		aChangedFlag = true;

		return true;
	}

	#endregion

	#region Implementation of INotifyPropertyChanged

	public event PropertyChangedEventHandler? PropertyChanged;

	#endregion

	#region Implementation

	protected bool SetProperty<TProperty>(ref TProperty aStorage, TProperty aValue, [CallerMemberName] string? aPropertyName = null)
	{
		if(Equals(aStorage, aValue))
			return false;

		aStorage = aValue;

		// ReSharper disable ExplicitCallerInfoArgument
		RaisePropertyChanged(aPropertyName);
		// ReSharper restore ExplicitCallerInfoArgument

		return true;
	}

	/// <summary>
	/// When other properties have to notify change.
	/// Two benefits:
	/// 1) you wont get the Resharper warning ExplicitCallerInfoArgument
	/// 2) you see that you notify an other Property (e.g. outside of a setter)
	/// </summary>
	/// <param name="aPropertyName"></param>
	[SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "Das muss vom Erbling aufgerufen werden können. Das geht mit events nicht.")]
	protected void RaiseOtherPropertyChanged(string? aPropertyName)
		// ReSharper disable once ExplicitCallerInfoArgument
		=> RaisePropertyChanged(aPropertyName);

	[SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "Das muss vom Erbling aufgerufen werden können. Das geht mit events nicht.")]
	protected void RaisePropertyChanged([CallerMemberName] string? aPropertyName = null)
		=> PropertyChanged?.Invoke(this, new(aPropertyName));

	#endregion
}