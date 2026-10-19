using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Center.Common;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning;

    public async void Execute(object? parameter)
    {
        if (_isRunning)
            return;

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            // The ViewModel handles user-facing errors; this is only a safety net
            // so an async void command can never tear the process down.
            Debug.WriteLine(ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
