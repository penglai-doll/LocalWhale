using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using LocalWhale.Core.Logging;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;
using LocalWhale.Core.Plugins;
using LocalWhale.Core.Runtime;
using LocalWhale.Core.Updates;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace LocalWhale.App;

public sealed partial class MainWindow : Window
{
    private readonly LocalWhalePaths _paths = new();
    private readonly FileLogger _logger;
    private readonly AppSettingsStore _settingsStore;
    private readonly AtomicJsonFile<RuntimeState> _runtimeStateStore;
    private readonly HarnessRuntimeManager _runtimeManager;
    private readonly HttpClient _updateHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HarnessUpdateService _updateService;
    private readonly PluginCompatibilityService _pluginCompatibilityService = new();
    private readonly ShellUpdateService _shellUpdateService;
    private readonly ShellThemeService _themeService;
    private readonly AppWindow _appWindow;
    private readonly NativeTrayIcon _trayIcon;
    private readonly DispatcherQueue _uiDispatcher;
    private AppSettings _settings = AppSettings.Default;
    private RuntimeState _runtimeState = new(LocalWhalePaths.InitialHarnessVersion, null, new Dictionary<string, int>());
    private bool _loaded;
    private bool _exiting;
    private bool _automaticUpdateCheckStarted;
    private HarnessUpdate? _availableUpdate;
    private StagedRuntime? _stagedRuntime;
    private ShellUpdate? _availableShellUpdate;
    private StagedShellUpdate? _stagedShellUpdate;
    private bool _automaticShellUpdateCheckStarted;
    private bool _shellUpdateDownloadRunning;
    private bool _toastUnavailable;
    private CancellationTokenSource? _stableRuntimeCancellation;
    private BitmapImage? _whaleGirlPortraitSource;

    private static readonly Uri WhaleGirlPortraitUri =
        new("ms-appx:///Assets/Themes/WhaleGirl/WhaleGirlPortrait.png", UriKind.Absolute);

    public MainWindow()
    {
        ShowWindowCommand = new RelayCommand(ShowAndActivate);
        RestartHarnessCommand = new RelayCommand(() => _ = RestartHarnessAsync());
        ExitCommand = new RelayCommand(() => _ = ExitAsync());
        _uiDispatcher = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        _themeService = new ShellThemeService();

        _logger = new FileLogger(_paths.LogsDirectory);
        _settingsStore = new AppSettingsStore(_paths.SettingsFile);
        _runtimeStateStore = new AtomicJsonFile<RuntimeState>(_paths.RuntimeStateFile);
        _runtimeManager = new HarnessRuntimeManager(CreateLaunchSpec, TimeSpan.FromSeconds(30), _logger.Write);
        _runtimeManager.UnexpectedExit += RuntimeManager_UnexpectedExit;
        _updateService = new HarnessUpdateService(
            new NpmRegistryClient(_updateHttpClient),
            new PnpmRuntimeInstaller(_paths, _logger, ValidateCandidateInWebViewAsync),
            _settingsStore,
            _runtimeStateStore);
        _shellUpdateService = new ShellUpdateService(
            new GitHubReleaseClient(_updateHttpClient),
            _updateHttpClient,
            _settingsStore,
            GetShellVersion(),
            _paths.UpdatesDirectory);
        TryRegisterToastNotifier();

        Title = "LocalWhale";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        var applicationIconPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Assets", "LocalWhale.ico"));
        _appWindow.SetIcon(applicationIconPath);
        _trayIcon = new NativeTrayIcon(
            windowHandle,
            applicationIconPath,
            ShowAndActivate,
            () => _ = RestartHarnessAsync(),
            () => _ = ExitAsync());
        _appWindow.Resize(new SizeInt32(1240, 800));
        _appWindow.SetPresenter(AppWindowPresenterKind.Default);
        _appWindow.Closing += AppWindow_Closing;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    public ICommand ShowWindowCommand { get; }
    public ICommand RestartHarnessCommand { get; }
    public ICommand ExitCommand { get; }

    public void ShowAndActivate()
    {
        _appWindow.Show();
        Activate();
    }

    public void ShowFatalError(Exception exception)
    {
        _logger.Write($"Unhandled exception: {exception}");
        StartupProgress.IsActive = false;
        StartupTitle.Text = "LocalWhale 遇到了未处理的错误";
        StartupDetail.Text = LogRedactor.Redact(exception.Message);
        RecoveryActions.Visibility = Visibility.Visible;
        StartupOverlay.Visibility = Visibility.Visible;
        ShowAndActivate();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        _settings = await _settingsStore.LoadAsync();
        _themeService.Apply(_settings.VisualTheme);
        UpdateThemeArtwork();
        await StartHarnessAsync();
    }

    private async Task StartHarnessAsync()
    {
        _stableRuntimeCancellation?.Cancel();
        _stableRuntimeCancellation?.Dispose();
        _stableRuntimeCancellation = new CancellationTokenSource();
        SetStartingState("正在唤醒 Harness", "后台服务通过健康检查后会自动进入界面");
        try
        {
            _runtimeState = await _runtimeStateStore.LoadAsync() ?? _runtimeState;
            var runtime = await HarnessStartupCoordinator.StartAsync(
                _runtimeManager,
                _runtimeState.ActiveVersion,
                GetDshHome(),
                _logger.Write,
                CancellationToken.None);
            _ = MarkRuntimeStableAsync(runtime, _stableRuntimeCancellation.Token);
            SetConnectedState(runtime);

            if (HarnessWebView.CoreWebView2 is null)
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, _paths.WebView2Directory, null);
                await HarnessWebView.EnsureCoreWebView2Async(environment);
                var core = HarnessWebView.CoreWebView2
                    ?? throw new InvalidOperationException("The main WebView2 did not initialize.");
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.ProcessFailed += (_, args) =>
                    DispatcherQueue.TryEnqueue(() => ShowFatalError(new InvalidOperationException($"WebView2 process failed: {args.ProcessFailedKind}")));
            }

            // A WebView2 control is initialized exactly once; after the candidate
            // smoke introduced a second environment identity into this process,
            // re-calling EnsureCoreWebView2Async throws ArgumentException, so an
            // already-initialized control only ever navigates to the new runtime.
            HarnessWebView.Source = runtime.BaseUri;
        }
        catch (Exception exception)
        {
            _logger.Write($"Harness startup failed: {exception}");
            var transition = RuntimeStatePolicy.RegisterFailedStart(_runtimeState, _runtimeState.ActiveVersion);
            _runtimeState = transition.State;
            await _runtimeStateStore.SaveAsync(_runtimeState);
            if (transition.RolledBack)
            {
                await StartHarnessAsync();
                return;
            }
            SetRecoveryState(
                "Harness 启动失败",
                LogRedactor.Redact(exception.Message));
        }
    }

    private void RuntimeManager_UnexpectedExit(object? sender, HarnessUnexpectedExit args)
    {
        DispatcherQueue.TryEnqueue(async () => await HandleUnexpectedExitAsync(args));
    }

    private async Task HandleUnexpectedExitAsync(HarnessUnexpectedExit args)
    {
        if (_exiting) return;
        _stableRuntimeCancellation?.Cancel();
        _logger.Write($"Harness {args.Version} exited unexpectedly with code {args.ExitCode} after {args.Uptime}.");
        var transition = RuntimeStatePolicy.RegisterUnexpectedExit(_runtimeState, args.Version, args.Uptime);
        _runtimeState = transition.State;
        await _runtimeStateStore.SaveAsync(_runtimeState);

        if (transition.RolledBack)
        {
            SetStartingState("新版 Harness 已自动回滚", $"{args.Version} 连续异常退出，正在恢复 {_runtimeState.ActiveVersion}。");
            await _runtimeManager.StopAsync(CancellationToken.None);
            await StartHarnessAsync();
            return;
        }

        if (args.Uptime < TimeSpan.FromSeconds(60) && !string.IsNullOrWhiteSpace(_runtimeState.PreviousVersion))
        {
            SetStartingState("Harness 意外退出", "正在自动重试；若新版再次早退，将恢复上一已知良好版本。");
            await _runtimeManager.StopAsync(CancellationToken.None);
            await StartHarnessAsync();
            return;
        }

        SetRecoveryState("Harness 意外退出", $"进程退出代码 {args.ExitCode}。可以重试，日志中保留了脱敏后的启动输出。");
    }

    private async Task MarkRuntimeStableAsync(HarnessRuntimeInfo runtime, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            if (_runtimeManager.CurrentRuntime?.ProcessId != runtime.ProcessId || !_runtimeManager.IsRunning) return;
            _runtimeState = RuntimeStatePolicy.RegisterHealthyStart(_runtimeState);
            await _runtimeStateStore.SaveAsync(_runtimeState, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string GetDshHome()
    {
        var dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
        if (string.IsNullOrWhiteSpace(dshHome))
        {
            dshHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dsh");
        }

        return Path.GetFullPath(dshHome);
    }

    /// <summary>
    /// Reads dsh-plugin.json manifests under the dsh home (never other files) and reports per-plugin
    /// admission states for one Harness version, per the vendored dsh-ecosystem-spec. Failures are
    /// logged only; they never gate the update itself.
    /// </summary>
    private string? DescribePluginCompatibility(string harnessVersion)
    {
        try
        {
            var compatibility = _pluginCompatibilityService.Evaluate(harnessVersion, GetDshHome());
            foreach (var result in compatibility.Results)
            {
                _logger.Write(
                    $"Plugin admission for {harnessVersion}: {result.Manifest.Id}@{result.Manifest.PluginVersion} -> {result.State} ({result.ReasonCode})" +
                    (result.MissingOptionalContracts.Count > 0 ? $" missing optional: {string.Join(", ", result.MissingOptionalContracts)}" : string.Empty) +
                    (result.DeniedPermissions.Count > 0 ? $" denied permissions: {string.Join(", ", result.DeniedPermissions)}" : string.Empty) +
                    (result.UnknownContracts.Count > 0 ? $" unknown contracts: {string.Join(", ", result.UnknownContracts)}" : string.Empty));
            }

            foreach (var error in compatibility.ScanErrors)
            {
                _logger.Write($"Plugin manifest unreadable ({error.SourcePath}): {error.Message}");
            }

            return PluginCompatibilityService.Describe(compatibility);
        }
        catch (Exception exception)
        {
            _logger.Write($"Plugin compatibility check skipped: {exception.Message}");
            return null;
        }
    }

    private HarnessLaunchSpec CreateLaunchSpec(string version, string token)
    {
        var runtimeDirectory = _paths.ResolveHarnessDirectory(version);
        return new HarnessLaunchSpec(
            _paths.NodeExecutable,
            Path.Combine(runtimeDirectory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
            Path.Combine(runtimeDirectory, "desktop-bridge.yml"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            token,
            HarnessVersion: version,
            BridgeModulePath: Path.Combine(runtimeDirectory, "node_modules", "@localwhale", "dsh-desktop-bridge", "src", "index.js"));
    }

    private void HarnessWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            SetRecoveryState("Harness 页面加载失败", $"WebView2 错误：{args.WebErrorStatus}");
            return;
        }

        StartupOverlay.Visibility = Visibility.Collapsed;
        _ = CompactManagedMemoryAfterStartupAsync();
        if (!_automaticUpdateCheckStarted)
        {
            _automaticUpdateCheckStarted = true;
            _ = AutomaticUpdateCheckAsync();
        }

        if (!_automaticShellUpdateCheckStarted)
        {
            _automaticShellUpdateCheckStarted = true;
            _ = AutomaticShellUpdateCheckAsync();
        }
    }

    private static async Task CompactManagedMemoryAfterStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private async Task ApplyCloseBehaviorAsync(CloseBehavior behavior)
    {
        _settings = _settings with { CloseBehavior = behavior };
        await _settingsStore.SaveAsync(_settings);
        if (behavior == CloseBehavior.MinimizeToTray) _trayIcon.Show();
    }

    private async Task SetVisualThemeAsync(VisualTheme theme)
    {
        _themeService.Apply(theme);
        _settings = _settings with { VisualTheme = theme };
        UpdateThemeArtwork();
        await _settingsStore.SaveAsync(_settings);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateThemeArtwork();

    private void UpdateThemeArtwork()
    {
        var isHighContrast = false;
        try
        {
            isHighContrast = new AccessibilitySettings().HighContrast;
        }
        catch
        {
            // Theme resources still provide the high-contrast fallback if the query is unavailable.
        }

        var showPortrait =
            _themeService.CurrentTheme == VisualTheme.WhaleGirl &&
            RootGrid.ActualWidth >= 760 &&
            !isHighContrast;

        if (showPortrait)
        {
            _whaleGirlPortraitSource ??= new BitmapImage(WhaleGirlPortraitUri);
            WhaleGirlPortrait.Source = _whaleGirlPortraitSource;
            WhaleGirlPortrait.Opacity =
                Application.Current.Resources["WhaleGirlDecorationOpacity"] is double opacity
                    ? opacity
                    : 0.9;
            WhaleGirlPortrait.Visibility = Visibility.Visible;
            WhaleGirlPortraitColumn.Width = new GridLength(320);
            return;
        }

        WhaleGirlPortrait.Source = null;
        WhaleGirlPortrait.Visibility = Visibility.Collapsed;
        WhaleGirlPortraitColumn.Width = new GridLength(0);
    }

    private void RestartHarnessButton_Click(object sender, RoutedEventArgs e) => _ = RestartHarnessAsync();

    private async Task RestartHarnessAsync()
    {
        SetStartingState("正在重启 Harness", "当前窗口会在服务恢复后重新连接");
        try
        {
            var runtime = await _runtimeManager.RestartAsync(CancellationToken.None);
            SetConnectedState(runtime);
            HarnessWebView.Source = runtime.BaseUri;
        }
        catch (Exception exception)
        {
            SetRecoveryState("Harness 重启失败", LogRedactor.Redact(exception.Message));
        }
    }

    private async Task AutomaticUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        await CheckForUpdatesAsync(manual: false);
    }

    private async Task<string> CheckForUpdatesAsync(bool manual)
    {
        if (manual)
        {
            UpdateInfoBar.Title = "正在检查 Harness 更新";
            UpdateInfoBar.Message = "正在联系官方 npm registry…";
            UpdateInfoBar.Severity = InfoBarSeverity.Informational;
            ApplyUpdateButton.IsEnabled = false;
            UpdateInfoBar.IsOpen = true;
        }

        try
        {
            _availableUpdate = await _updateService.CheckAsync(manual, CancellationToken.None);
            _settings = await _settingsStore.LoadAsync();
            if (_availableUpdate is null)
            {
                if (!manual)
                {
                    UpdateInfoBar.IsOpen = false;
                    return string.Empty;
                }

                UpdateInfoBar.Title = "Harness 已是最新版";
                UpdateInfoBar.Message = $"当前版本 {_runtimeState.ActiveVersion}，无需更新。";
                UpdateInfoBar.Severity = InfoBarSeverity.Informational;
                ApplyUpdateButton.IsEnabled = false;
                return $"Harness 已是最新版（当前 {_runtimeState.ActiveVersion}）。";
            }

            _stagedRuntime = null;
            var discoveredPluginSummary = DescribePluginCompatibility(_availableUpdate.AvailableVersion);
            UpdateInfoBar.Title = "发现 Harness 更新";
            UpdateInfoBar.Message = $"官方 npm 提供 {_availableUpdate.AvailableVersion}；更新前会在临时目录完成安装、脚本白名单和启动冒烟。"
                + (discoveredPluginSummary is null ? string.Empty : "\n" + discoveredPluginSummary);
            UpdateInfoBar.Severity = InfoBarSeverity.Informational;
            ApplyUpdateButton.Content = "更新";
            ApplyUpdateButton.IsEnabled = true;
            UpdateInfoBar.IsOpen = true;
            return $"发现 Harness 更新 {_availableUpdate.AvailableVersion}，可在右下角提示中更新。";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.Write($"Harness update check unavailable: {exception.Message}");
            if (manual)
            {
                UpdateInfoBar.Title = "暂时无法检查更新";
                UpdateInfoBar.Message = "npm registry 当前不可用；现用 Harness 不受影响。";
                UpdateInfoBar.Severity = InfoBarSeverity.Warning;
                ApplyUpdateButton.IsEnabled = false;
            }

            return "暂时无法检查更新：npm registry 当前不可用，现用 Harness 不受影响。";
        }
    }

    private void LaterUpdate_Click(object sender, RoutedEventArgs e) => UpdateInfoBar.IsOpen = false;

    private async void IgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
        {
            _settings = _settings with
            {
                IgnoredHarnessVersions = _settings.IgnoredHarnessVersions
                    .Append(_availableUpdate.AvailableVersion)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
        UpdateInfoBar.IsOpen = false;
        await _settingsStore.SaveAsync(_settings);
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        ApplyUpdateButton.IsEnabled = false;
        try
        {
            if (_stagedRuntime is null)
            {
                UpdateInfoBar.Title = "正在验证 Harness 更新";
                UpdateInfoBar.Message = "正在生成锁文件、校验 integrity、离线安装并启动候选实例；当前会话不会中断。";
                UpdateInfoBar.Severity = InfoBarSeverity.Informational;
                _stagedRuntime = await _updateService.StageAndValidateAsync(_availableUpdate.AvailableVersion, CancellationToken.None);
                var pluginSummary = DescribePluginCompatibility(_stagedRuntime.Version);
                UpdateInfoBar.Title = "Harness 更新已验证";
                UpdateInfoBar.Message = $"{_stagedRuntime.Version} 已通过 bridge、首页插件和优雅关闭冒烟。可以重启切换。"
                    + (pluginSummary is null ? string.Empty : "\n" + pluginSummary);
                UpdateInfoBar.Severity = InfoBarSeverity.Success;
                ApplyUpdateButton.Content = "重启并应用";
                ApplyUpdateButton.IsEnabled = true;
                return;
            }

            await _updateService.ActivateOnRestartAsync(_stagedRuntime, CancellationToken.None);
            _runtimeState = await _runtimeStateStore.LoadAsync() ?? _runtimeState;
            UpdateInfoBar.IsOpen = false;
            await _runtimeManager.StopAsync(CancellationToken.None);
            await StartHarnessAsync();
        }
        catch (Exception exception)
        {
            _logger.Write($"Harness update failed: {exception}");
            UpdateInfoBar.Title = "Harness 更新失败";
            UpdateInfoBar.Message = LogRedactor.Redact(exception.Message) + "\n现用版本保持不变。";
            UpdateInfoBar.Severity = InfoBarSeverity.Error;
            ApplyUpdateButton.IsEnabled = false;
        }
    }

    private void TryRegisterToastNotifier()
    {
        try
        {
            AppNotificationManager.Default.Register();
        }
        catch (Exception exception)
        {
            _toastUnavailable = true;
            _logger.Write($"Toast notifications unavailable: {exception.Message}");
        }
    }

    private void TryShowShellUpdateToast()
    {
        if (_toastUnavailable || _availableShellUpdate is null) return;
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("LocalWhale 本地客户端有新版本")
                .AddText($"v{_availableShellUpdate.AvailableVersion} 已发布；下载校验完成后，重启 LocalWhale 即自动安装。")
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            _toastUnavailable = true;
            _logger.Write($"Shell update toast could not be shown: {exception.Message}");
        }
    }

    private async Task AutomaticShellUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await CheckForShellUpdatesAsync(manual: false);
    }

    private async Task<string> CheckForShellUpdatesAsync(bool manual)
    {
        try
        {
            if (_stagedShellUpdate is not null)
            {
                return $"本地客户端更新已就绪（v{_stagedShellUpdate.Version}）：重启 LocalWhale 后自动安装。";
            }

            _availableShellUpdate = await _shellUpdateService.CheckAsync(manual, CancellationToken.None);
            _settings = await _settingsStore.LoadAsync();
            if (_availableShellUpdate is null)
            {
                return manual ? $"本地客户端已是最新版（当前 {GetShellVersion()}）。" : string.Empty;
            }

            ShowShellUpdateDiscovered();
            TryShowShellUpdateToast();
            return $"发现本地客户端更新 v{_availableShellUpdate.AvailableVersion}，可在右下角提示中下载。";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            _logger.Write($"Shell update check unavailable: {exception.Message}");
            return manual ? "暂时无法检查本地客户端更新：GitHub 当前不可访问，现用版本不受影响。" : string.Empty;
        }
    }

    private void ShowShellUpdateDiscovered()
    {
        ShellUpdateCard.Severity = InfoBarSeverity.Informational;
        ShellUpdateCard.Title = $"发现本地客户端新版本 v{_availableShellUpdate!.AvailableVersion}";
        ShellUpdateCard.Message = $"当前 {GetShellVersion()}；下载并完成 SHA-256 校验后，重启 LocalWhale 即自动安装。";
        ShellUpdateActionButton.Content = "下载更新";
        ShellUpdateActionButton.IsEnabled = true;
        IgnoreShellUpdateButton.Visibility = Visibility.Visible;
        ShellUpdateProgress.Value = 0;
        ShellUpdateProgress.Visibility = Visibility.Collapsed;
        ShellUpdateCard.IsOpen = true;
    }

    private async Task ApplyShellUpdateCheckEnabledAsync(bool enabled)
    {
        _settings = _settings with { ShellUpdateCheckEnabled = enabled };
        await _settingsStore.SaveAsync(_settings);
    }

    private async void ShellUpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stagedShellUpdate is not null)
        {
            ShellUpdateCard.IsOpen = false;
            await ExitAsync();
            return;
        }

        if (_availableShellUpdate is null || _shellUpdateDownloadRunning) return;
        _shellUpdateDownloadRunning = true;
        ShellUpdateActionButton.IsEnabled = false;
        ShellUpdateActionButton.Content = "正在下载…";
        ShellUpdateCard.Severity = InfoBarSeverity.Informational;
        ShellUpdateCard.Title = "正在下载本地客户端更新";
        ShellUpdateCard.Message = $"v{_availableShellUpdate.AvailableVersion} · 正在校验下载源…";
        ShellUpdateProgress.Value = 0;
        ShellUpdateProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(percent =>
        {
            ShellUpdateProgress.Value = percent;
            ShellUpdateCard.Message = $"v{_availableShellUpdate?.AvailableVersion} · {percent:F0}%（下载后自动校验 SHA-256）";
        });
        try
        {
            _stagedShellUpdate = await _shellUpdateService.DownloadAndStageAsync(_availableShellUpdate, progress, CancellationToken.None);
            ShellUpdateCard.Severity = InfoBarSeverity.Success;
            ShellUpdateCard.Title = "本地客户端更新已就绪";
            ShellUpdateCard.Message = $"v{_stagedShellUpdate.Version} 已通过 SHA-256 校验；重启 LocalWhale 后自动安装并回到新版本。";
            ShellUpdateActionButton.Content = "立即重启";
            ShellUpdateActionButton.IsEnabled = true;
            IgnoreShellUpdateButton.Visibility = Visibility.Collapsed;
            ShellUpdateProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            _logger.Write($"Shell update download failed: {exception}");
            ShellUpdateCard.Severity = InfoBarSeverity.Error;
            ShellUpdateCard.Title = "本地客户端更新下载失败";
            ShellUpdateCard.Message = $"{LogRedactor.Redact(exception.Message)}\n当前版本不受影响，可稍后重试。";
            ShellUpdateActionButton.Content = "重试";
            ShellUpdateActionButton.IsEnabled = true;
            ShellUpdateProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _shellUpdateDownloadRunning = false;
        }
    }

    private async void IgnoreShellUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableShellUpdate is not null)
        {
            _settings = _settings with
            {
                IgnoredShellVersions = (_settings.IgnoredShellVersions ?? Array.Empty<string>())
                    .Append(_availableShellUpdate.AvailableVersion)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
            if (_stagedShellUpdate is not null && string.Equals(
                    _stagedShellUpdate.Version,
                    _availableShellUpdate.AvailableVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                _shellUpdateService.ClearStagedUpdate();
                _stagedShellUpdate = null;
            }

            _availableShellUpdate = null;
            await _settingsStore.SaveAsync(_settings);
        }

        ShellUpdateCard.IsOpen = false;
    }

    private void LaterShellUpdate_Click(object sender, RoutedEventArgs e) => ShellUpdateCard.IsOpen = false;

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsFolder();

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        Process.Start(new ProcessStartInfo { FileName = _paths.LogsDirectory, UseShellExecute = true });
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await ShowSettingsAsync();

    private async Task ShowSettingsAsync()
    {
        var dialog = new SettingsDialog(
            new SettingsDialogState(
                _settings.CloseBehavior,
                _themeService.CurrentTheme,
                GetShellVersion(),
                _runtimeState.ActiveVersion,
                _settings.ShellUpdateCheckEnabled),
            new SettingsDialogCallbacks(
                ApplyCloseBehaviorAsync,
                SetVisualThemeAsync,
                ApplyShellUpdateCheckEnabledAsync,
                () => CheckForShellUpdatesAsync(manual: true),
                () => CheckForUpdatesAsync(manual: true),
                OpenLogsFolder))
        {
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
        if (dialog.AboutRequested) await ShowAboutDialogAsync();
    }

    private async Task ShowAboutDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "LocalWhale",
            Content = CreateAboutContent(),
            CloseButtonText = "关闭"
        };
        await dialog.ShowAsync();
    }

    private FrameworkElement CreateAboutContent()
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        identity.Children.Add(new Image
        {
            Width = 48,
            Height = 48,
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Brand/LocalWhaleMark-32.png"))
        });
        var versions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        versions.Children.Add(new TextBlock
        {
            Text = "LocalWhale",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        versions.Children.Add(new TextBlock
        {
            Text = $"Shell {GetShellVersion()} · Harness {_runtimeState.ActiveVersion}",
            Foreground = (Brush)Application.Current.Resources["ShellMutedForegroundBrush"]
        });
        identity.Children.Add(versions);
        content.Children.Add(identity);
        content.Children.Add(new TextBlock
        {
            Text = "LocalWhale 是独立社区本地客户端；官方 Harness WebUI 保持原样，用户数据仍由 ~/.dsh 管理。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "GitHub · penglai-doll/LocalWhale",
            NavigateUri = new Uri("https://github.com/penglai-doll/LocalWhale"),
            Padding = new Thickness(0)
        });

        if (_themeService.CurrentTheme == VisualTheme.WhaleGirl)
        {
            _whaleGirlPortraitSource ??= new BitmapImage(WhaleGirlPortraitUri);
            var character = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };
            character.Children.Add(new Image
            {
                Width = 88,
                Height = 100,
                Source = _whaleGirlPortraitSource,
                Stretch = Stretch.Uniform
            });
            character.Children.Add(new TextBlock
            {
                Text = "LocalWhale 原创社区角色（AI 辅助设计）",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260
            });
            content.Children.Add(character);
        }

        return content;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        await _runtimeManager.StopAsync(CancellationToken.None);
        await StartHarnessAsync();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        if (_settings.CloseBehavior == CloseBehavior.MinimizeToTray)
        {
            _trayIcon.Show();
            _appWindow.Hide();
            return;
        }

        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            _stableRuntimeCancellation?.Cancel();
            await _runtimeManager.StopAsync(CancellationToken.None);
            _trayIcon.Dispose();
            _updateHttpClient.Dispose();
            _stableRuntimeCancellation?.Dispose();
        }
        finally
        {
            Close();
            (Application.Current as App)?.ExitApplication();
        }
    }

    private void SetStartingState(string title, string detail)
    {
        StartupTitle.Text = title;
        StartupDetail.Text = detail;
        StartupProgress.IsActive = true;
        RecoveryActions.Visibility = Visibility.Collapsed;
        StartupOverlay.Visibility = Visibility.Visible;
        ConnectionText.Text = "正在启动";
        StatusDot.Fill = new SolidColorBrush(Colors.Goldenrod);
    }

    private void SetConnectedState(HarnessRuntimeInfo runtime)
    {
        ConnectionText.Text = "已连接";
        AppTitleBar.Subtitle = $"LocalWhale {GetShellVersion()} · Harness {runtime.Version}";
        StatusDot.Fill = new SolidColorBrush(Colors.LimeGreen);
    }

    private void SetRecoveryState(string title, string detail)
    {
        StartupTitle.Text = title;
        StartupDetail.Text = detail;
        StartupProgress.IsActive = false;
        RecoveryActions.Visibility = Visibility.Visible;
        StartupOverlay.Visibility = Visibility.Visible;
        ConnectionText.Text = "启动失败";
        StatusDot.Fill = new SolidColorBrush(Colors.IndianRed);
    }

    private static string GetShellVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.4";

    private Task ValidateCandidateInWebViewAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        // PnpmRuntimeInstaller runs its pipeline on thread-pool continuations
        // (ConfigureAwait(false)); WinUI XAML controls may only be created and
        // touched on the UI thread, so marshal the whole validation over.
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_uiDispatcher.TryEnqueue(() => _ = RunOnUiAsync(completion, baseUri, cancellationToken)))
        {
            completion.SetException(new InvalidOperationException("The UI dispatcher was unavailable for the candidate WebView validation."));
        }
        return completion.Task;
    }

    private async Task RunOnUiAsync(TaskCompletionSource<object?> completion, Uri baseUri, CancellationToken cancellationToken)
    {
        try
        {
            await ValidateCandidateOnUiThreadAsync(baseUri, cancellationToken);
            completion.SetResult(null);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private async Task ValidateCandidateOnUiThreadAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var candidateData = Path.Combine(_paths.LocalDataDirectory, "webview2-candidate", Guid.NewGuid().ToString("N"));
        var webView = new WebView2
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(webView, 1);
        RootGrid.Children.Add(webView);
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, candidateData, null);
            await webView.EnsureCoreWebView2Async(environment);
            var navigation = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.NavigationCompleted += (_, args) => navigation.TrySetResult(args);
            webView.Source = baseUri;
            var result = await navigation.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            if (!result.IsSuccess) throw new InvalidDataException($"Candidate WebView navigation failed: {result.WebErrorStatus}");

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var json = await webView.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify({boot:Boolean(window.__DSH_BOOT__),root:Boolean(document.querySelector('#root')?.children.length)})");
                if (json.Contains("\\\"boot\\\":true", StringComparison.Ordinal) && json.Contains("\\\"root\\\":true", StringComparison.Ordinal)) return;
                await Task.Delay(250, cancellationToken);
            }

            throw new InvalidDataException("Candidate Harness frontend did not render its root client plugins.");
        }
        finally
        {
            webView.Close();
            RootGrid.Children.Remove(webView);
            await CleanupCandidateWebViewAsync(candidateData);
        }
    }

    private async Task CleanupCandidateWebViewAsync(string candidateData)
    {
        var candidateRoot = Path.Combine(_paths.LocalDataDirectory, "webview2-candidate");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                TemporaryDirectoryCleaner.DeleteTreeWithin(candidateRoot, candidateData);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    _logger.Write($"Candidate WebView2 data could not be removed: {candidateData}: {exception.Message}");
                    return;
                }
                await Task.Delay(250);
            }
        }
    }
}
