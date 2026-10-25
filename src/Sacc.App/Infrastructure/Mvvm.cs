using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Sacc.App.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public RelayCommand(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
}

/// <summary>Async command that swallows nothing: the handler is expected to report its own errors.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _run;
    private readonly Func<object?, bool>? _can;
    private bool _running;

    public AsyncCommand(Func<object?, Task> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public AsyncCommand(Func<Task> run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (_can?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _run(parameter); }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
