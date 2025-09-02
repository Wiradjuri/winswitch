using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WinSwitch.Utilities;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? can = null)
    {
        _executeAsync = _ => executeAsync();
        _canExecute = can == null ? null : (_ => can());
    }

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _isRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _executeAsync(parameter); }
        finally { _isRunning = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
