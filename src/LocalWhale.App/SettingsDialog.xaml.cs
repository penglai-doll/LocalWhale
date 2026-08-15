using LocalWhale.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LocalWhale.App;

public sealed record SettingsDialogState(
    CloseBehavior CloseBehavior,
    VisualTheme VisualTheme,
    string ShellVersion,
    string HarnessVersion,
    bool ShellUpdateCheckEnabled);

public sealed record SettingsDialogCallbacks(
    Func<CloseBehavior, Task> CloseBehaviorChanged,
    Func<VisualTheme, Task> ThemeChanged,
    Func<bool, Task> ShellUpdateCheckEnabledChanged,
    Func<Task<string>> CheckShellUpdates,
    Func<Task<string>> CheckHarnessUpdates,
    Action OpenLogs);

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly SettingsDialogCallbacks _callbacks;
    private bool _initialized;
    private bool _checkingShellUpdates;

    private async void ShellUpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        await _callbacks.ShellUpdateCheckEnabledChanged(ShellUpdateCheckToggle.IsOn);
    }

    private async void CheckShellUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingShellUpdates) return;
        _checkingShellUpdates = true;
        CheckShellUpdatesButton.IsEnabled = false;
        ShellCheckStatusText.Text = "正在检查外壳更新…";
        try
        {
            ShellCheckStatusText.Text = await _callbacks.CheckShellUpdates();
        }
        finally
        {
            _checkingShellUpdates = false;
            CheckShellUpdatesButton.IsEnabled = true;
        }
    }

    private bool _checkingHarnessUpdates;

    /// <summary>Set when the user asked for the About dialog; the owner shows it after this dialog closes.</summary>
    public bool AboutRequested { get; private set; }

    public SettingsDialog(SettingsDialogState state, SettingsDialogCallbacks callbacks)
    {
        InitializeComponent();
        _callbacks = callbacks;
        CloseToTrayToggle.IsOn = state.CloseBehavior == CloseBehavior.MinimizeToTray;
        HarnessVersionText.Text = $"当前 {state.HarnessVersion}";
        ShellVersionText.Text = $"Shell {state.ShellVersion} · 独立社区项目，与 DeepSeek 无隶属关系";
        ShellVersionRowText.Text = $"当前 Shell {state.ShellVersion}（GitHub 最新发布）";
        ShellUpdateCheckToggle.IsOn = state.ShellUpdateCheckEnabled;
        if (state.VisualTheme == VisualTheme.WhaleGirl)
        {
            WhaleGirlThemeRadio.IsChecked = true;
        }
        else
        {
            OriginalThemeRadio.IsChecked = true;
        }

        _initialized = true;
    }

    private async void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        await _callbacks.CloseBehaviorChanged(CloseToTrayToggle.IsOn ? CloseBehavior.MinimizeToTray : CloseBehavior.Exit);
    }

    private async void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var theme = ReferenceEquals(sender, WhaleGirlThemeRadio) ? VisualTheme.WhaleGirl : VisualTheme.Original;
        await _callbacks.ThemeChanged(theme);
    }

    private async void CheckHarnessUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingHarnessUpdates) return;
        _checkingHarnessUpdates = true;
        CheckHarnessUpdatesButton.IsEnabled = false;
        HarnessCheckStatusText.Text = "正在检查 Harness 更新…";
        try
        {
            HarnessCheckStatusText.Text = await _callbacks.CheckHarnessUpdates();
        }
        finally
        {
            _checkingHarnessUpdates = false;
            CheckHarnessUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) => _callbacks.OpenLogs();

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        AboutRequested = true;
        Hide();
    }
}
