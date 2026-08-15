using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;
using LocalWhale.Core.Runtime;
using LocalWhale.Core.Updates;
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

            if (await TryLaunchStagedShellUpdateAsync())
            {
                // The detached installer waits for this process to exit, then silently
                // reinstalls and relaunches via /RESTARTAPP.
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

    private static async Task<bool> TryLaunchStagedShellUpdateAsync()
    {
        var paths = new LocalWhalePaths();
        var updatesDirectory = paths.UpdatesDirectory;
        StagedShellUpdate? staged = null;
        try
        {
            staged = await StagedShellUpdateStore.LoadAsync(updatesDirectory);
            if (staged is null) return false;
            if (!File.Exists(staged.SetupPath))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            if (!TryCompareVersions(currentVersion, staged.Version, out var isNewer) || !isNewer)
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            if (!File.Exists(Path.Combine(paths.InstallDirectory, "LocalWhale.exe")))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            var actualSha256 = await ComputeSha256HexAsync(staged.SetupPath);
            if (!string.Equals(actualSha256, staged.SetupSha256, StringComparison.Ordinal))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            var commandShell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var installerArguments =
                $"/c ping -n 3 127.0.0.1 >nul & \"{staged.SetupPath}\" /SILENT /SP- /DIR=\"{paths.InstallDirectory}\" /RESTARTAPP";
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = commandShell,
                Arguments = installerArguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return installer is not null;
        }
        catch (Exception)
        {
            // A failed staged install must never block normal startup.
            if (staged is not null) ClearStagedShellUpdate(updatesDirectory, staged);
            return false;
        }
    }

    private static bool TryCompareVersions(string currentVersion, string targetVersion, out bool targetIsNewer)
    {
        try
        {
            targetIsNewer = SemVersion.Parse(targetVersion.TrimStart('v')) > SemVersion.Parse(currentVersion);
            return true;
        }
        catch (FormatException)
        {
            targetIsNewer = false;
            return false;
        }
    }

    private static void ClearStagedShellUpdate(string updatesDirectory, StagedShellUpdate staged)
    {
        StagedShellUpdateStore.Clear(updatesDirectory);
        try
        {
            File.Delete(staged.SetupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover payloads are removed by the uninstaller with %LOCALAPPDATA%\LocalWhale.
        }
    }

    private static async Task<string> ComputeSha256HexAsync(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
