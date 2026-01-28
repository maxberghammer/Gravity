using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Gravity.Wpf.Commands;

/// <summary>
/// A command that executes an async action and handles exceptions.
/// </summary>
internal sealed class AsyncRelayCommand : ICommand
{
	#region Fields

	private readonly Func<Task> _execute;
	private readonly Func<bool>? _canExecute;
	private bool _isExecuting;

	#endregion

	#region Construction

	public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		_canExecute = canExecute;
	}

	#endregion

	#region Implementation of ICommand

	public event EventHandler? CanExecuteChanged
	{
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}

	public bool CanExecute(object? parameter)
		=> !_isExecuting && (_canExecute?.Invoke() ?? true);

	[SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "ICommand.Execute must be void")]
	public async void Execute(object? parameter)
	{
		if(!CanExecute(parameter))
			return;

		_isExecuting = true;
		RaiseCanExecuteChanged();

		try
		{
			await _execute();
		}
		finally
		{
			_isExecuting = false;
			RaiseCanExecuteChanged();
		}
	}

	#endregion

	#region Implementation

	public static void RaiseCanExecuteChanged()
		=> CommandManager.InvalidateRequerySuggested();

	#endregion
}
