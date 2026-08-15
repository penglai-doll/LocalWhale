using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TemporaryTestDirectory _directory = new();
    private string SettingsPath => Path.Combine(_directory.Path, "settings.json");

    [Fact]
    public async Task LoadAsync_migrates_v010_settings_to_original_theme()
    {
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "closeBehavior": 1,
              "ignoredHarnessVersions": ["kept"],
              "lastUpdateCheckUtc": null
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await new AppSettingsStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
        Assert.Equal(["kept"], settings.IgnoredHarnessVersions);
        Assert.Equal(VisualTheme.Original, settings.VisualTheme);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("99")]
    [InlineData("\"FutureTheme\"")]
    [InlineData("{}")]
    public async Task LoadAsync_preserves_other_settings_when_visual_theme_is_invalid(string invalidTheme)
    {
        await File.WriteAllTextAsync(
            SettingsPath,
            $$"""
            {
              "closeBehavior": 1,
              "ignoredHarnessVersions": ["kept"],
              "lastUpdateCheckUtc": null,
              "visualTheme": {{invalidTheme}}
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await new AppSettingsStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
        Assert.Equal(["kept"], settings.IgnoredHarnessVersions);
        Assert.Equal(VisualTheme.Original, settings.VisualTheme);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"WhaleGirl\"")]
    [InlineData("\"whalegirl\"")]
    public async Task LoadAsync_accepts_supported_numeric_and_string_whale_girl_values(string serializedTheme)
    {
        await File.WriteAllTextAsync(
            SettingsPath,
            $$"""
            {
              "closeBehavior": 0,
              "ignoredHarnessVersions": [],
              "lastUpdateCheckUtc": null,
              "visualTheme": {{serializedTheme}}
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await new AppSettingsStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VisualTheme.WhaleGirl, settings.VisualTheme);
    }

    [Fact]
    public async Task LoadAsync_migrates_v011_settings_to_shell_update_defaults()
    {
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "closeBehavior": 1,
              "ignoredHarnessVersions": ["kept"],
              "lastUpdateCheckUtc": null,
              "visualTheme": "WhaleGirl"
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await new AppSettingsStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.ShellUpdateCheckEnabled);
        Assert.Null(settings.LastShellUpdateCheckUtc);
        Assert.NotNull(settings.IgnoredShellVersions);
        Assert.Empty(settings.IgnoredShellVersions);
        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
        Assert.Equal(VisualTheme.WhaleGirl, settings.VisualTheme);
    }

    [Fact]
    public async Task LoadAsync_accepts_explicitly_disabled_shell_update_checks()
    {
        await File.WriteAllTextAsync(
            SettingsPath,
            """
            {
              "closeBehavior": 0,
              "ignoredHarnessVersions": [],
              "lastUpdateCheckUtc": null,
              "visualTheme": "Original",
              "shellUpdateCheckEnabled": false,
              "ignoredShellVersions": ["0.1.3"]
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await new AppSettingsStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.ShellUpdateCheckEnabled);
        Assert.Equal(["0.1.3"], settings.IgnoredShellVersions);
    }

    [Fact]
    public async Task SaveAsync_round_trips_shell_update_settings()
    {
        var store = new AppSettingsStore(SettingsPath);
        var stagedAt = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var saved = new AppSettings(
            CloseBehavior.Exit,
            Array.Empty<string>(),
            null,
            VisualTheme.Original,
            ShellUpdateCheckEnabled: false,
            LastShellUpdateCheckUtc: stagedAt,
            IgnoredShellVersions: ["0.1.3", "0.1.4"]);
        await store.SaveAsync(saved, TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"shellUpdateCheckEnabled\": false", json);
        Assert.Contains("\"ignoredShellVersions\": [", json);
        Assert.False(loaded.ShellUpdateCheckEnabled);
        Assert.Equal(stagedAt, loaded.LastShellUpdateCheckUtc);
        Assert.Equal(["0.1.3", "0.1.4"], loaded.IgnoredShellVersions);
    }

    [Fact]
    public async Task SaveAsync_writes_canonical_theme_without_changing_close_behavior_encoding()
    {
        var store = new AppSettingsStore(SettingsPath);
        await store.SaveAsync(
            new AppSettings(CloseBehavior.MinimizeToTray, Array.Empty<string>(), null, VisualTheme.WhaleGirl),
            TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken);

        Assert.Contains("\"closeBehavior\": 1", json);
        Assert.Contains("\"visualTheme\": \"WhaleGirl\"", json);
    }

    public void Dispose() => _directory.Dispose();
}
