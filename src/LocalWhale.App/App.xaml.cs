using System.Runtime.InteropServices;
using LocalWhale.Core.Runtime;
using Microsoft.UI.Xaml;

namespace LocalWhale.App;

public partial class App : Application
{
    private MainWindow? _window;
    private SingleInstanceGate? _singleInstanceGate;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            WriteStartupFailure(args.Exception);
            _window?.ShowFatalError(args.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = LaunchAsync();

    public void ExitApplication() => Exit();

    private async Task LaunchAsync()
    {
        try
        {
            _singleInstanceGate = new SingleInstanceGate(@"Local\LocalWhale.MainWindow");
            if (!_singleInstanceGate.IsPrimary)
            {
                await ActivateExistingWindowAsync();
                Exit();
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteStartupFailure(ex);
            Exit();
        }
    }

    private static async Task ActivateExistingWindowAsync()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var handle = FindWindow(null, "LocalWhale");
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, ShowWindowRestore);
                SetForegroundWindow(handle);
                return;
            }

            await Task.Delay(200);
        }
    }

    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LocalWhale",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-fatal.log"),
                $"{DateTimeOffset.UtcNow:O} {exception}\n");
        }
        catch
        {
            // Startup diagnostics must never mask the original failure.
        }
    }

    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
