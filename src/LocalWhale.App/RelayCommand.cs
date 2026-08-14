using System.Windows.Input;

namespace LocalWhale.App;

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
