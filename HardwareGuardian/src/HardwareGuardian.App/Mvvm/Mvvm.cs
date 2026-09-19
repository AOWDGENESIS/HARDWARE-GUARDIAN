using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using HardwareGuardian.Core.Abstractions;

namespace HardwareGuardian.App.Mvvm;

/// <summary>Minimal INotifyPropertyChanged base. No behaviour beyond notification.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Base for every view model. It resolves all user visible text through the localizer and refreshes
/// the whole view model when the language changes, so already visible text switches immediately.
/// </summary>
public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed;

    protected ViewModelBase(ILocalizer localizer)
    {
        Localizer = localizer;
        Localizer.LanguageChanged += OnLanguageChanged;
    }

    protected ILocalizer Localizer { get; }

    /// <summary>Resolves a key. Missing keys stay visible as [[Key]] instead of showing empty text.</summary>
    protected string L(string key) => Localizer[key];

    protected string L(string key, params object?[] arguments) => Localizer.Format(key, arguments);

    protected string L(Core.Values.LocalizedText text) => Localizer.Resolve(text);

    protected string L(Core.Values.LocalizedText? text, string fallbackKey) => Localizer.Resolve(text, fallbackKey);

    /// <summary>True while a long running operation of this view model is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    private bool _isBusy;

    public bool CanInteract => !_isBusy;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Localizer.LanguageChanged -= OnLanguageChanged;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeCore()
    {
    }

    private void OnLanguageChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        // An empty property name refreshes every binding of this view model, including the strings
        // that were produced by the localizer at load time.
        OnPropertyChanged(string.Empty);
        OnLanguageChangedCore();
    }

    protected virtual void OnLanguageChangedCore()
    {
    }
}

/// <summary>Synchronous command for menu and button actions.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Asynchronous command. It blocks re-entry while running, because a double click must never start a
/// second maintenance run, backup or scan (spec section 22).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(true);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Collection that replaces its content in one notification.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void Reset(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
