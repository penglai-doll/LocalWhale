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
            if (!TryValidateStagedSetupPath(staged.SetupPath, updatesDirectory, out var setupPath))
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

            if (!IsSafePathArgument(paths.InstallDirectory))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            if (!File.Exists(Path.Combine(paths.InstallDirectory, "LocalWhale.exe")))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            var actualSha256 = await ComputeSha256HexAsync(setupPath);
            if (!string.Equals(actualSha256, staged.SetupSha256, StringComparison.Ordinal))
            {
                ClearStagedShellUpdate(updatesDirectory, staged);
                return false;
            }

            // Launch the installer directly with an argument string (never a shell):
            // the installer waits for the LocalWhale.MainWindow mutex to be released
            // before overwriting files, so no process-exit race delay is needed here.
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = $"/SILENT /SP- /DIR=\"{paths.InstallDirectory}\" /RESTARTAPP",
                UseShellExecute = false,
                CreateNoWindow = true
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

    /// <summary>
    /// The marker lives in user-writable storage, so the staged setup path is untrusted
    /// until it is confirmed to be the canonical setup file inside the updates directory.
    /// </summary>
    private static bool TryValidateStagedSetupPath(string setupPath, string updatesDirectory, out string validatedPath)
    {
        validatedPath = string.Empty;
        if (!IsSafePathArgument(setupPath)) return false;
        if (!Path.GetFileName(setupPath).Equals(ShellUpdateService.SetupAssetFileName, StringComparison.OrdinalIgnoreCase)) return false;

        var fullSetupPath = Path.GetFullPath(setupPath);
        var fullUpdatesRoot = Path.GetFullPath(updatesDirectory);
        if (!fullSetupPath.StartsWith(fullUpdatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;

        validatedPath = fullSetupPath;
        return true;
    }

    private static bool IsSafePathArgument(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            Path.IsPathRooted(path) &&
            path.IndexOfAny(['"', '\r', '\n', '\t']) < 0;
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
        // The marker is user-writable storage; only ever delete inside the updates directory.
        if (TryValidateStagedSetupPath(staged.SetupPath, updatesDirectory, out var setupPath))
        {
            try
            {
                File.Delete(setupPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Leftover payloads are removed by the uninstaller with %LOCALAPPDATA%\LocalWhale.
            }
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
