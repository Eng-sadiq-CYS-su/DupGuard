using System;
using System.Windows.Input;

namespace DupGuard.Utilities
{
    /// <summary>
    /// RelayCommand implementation for WPF commands
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action? _execute;
        private readonly Func<bool>? _canExecute;
        private readonly Action<object?>? _executeWithParameter;
        private readonly Func<object?, bool>? _canExecuteWithParameter;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _executeWithParameter = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecuteWithParameter = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecuteWithParameter != null)
                return _canExecuteWithParameter(parameter);

            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            if (_executeWithParameter != null)
            {
                _executeWithParameter(parameter);
                return;
            }

            _execute?.Invoke();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? CanExecuteChanged;
    }
}
