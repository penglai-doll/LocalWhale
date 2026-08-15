using System.Security.Cryptography;
using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Updates;

public sealed class ShellUpdateService : IShellUpdateService
{
    public const string SetupAssetFileName = "LocalWhale-Setup-x64.exe";
    public const string Sha256SumsAssetFileName = "SHA256SUMS.txt";

    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private readonly IGitHubReleaseClient _releaseClient;
    private readonly HttpClient _downloadClient;
    private readonly AppSettingsStore _settingsStore;
    private readonly string _currentShellVersion;
    private readonly string _updatesDirectory;

    public ShellUpdateService(
        IGitHubReleaseClient releaseClient,
        HttpClient downloadClient,
        AppSettingsStore settingsStore,
        string currentShellVersion,
        string updatesDirectory)
    {
        _releaseClient = releaseClient;
        _downloadClient = downloadClient;
        _settingsStore = settingsStore;
        _currentShellVersion = currentShellVersion;
        _updatesDirectory = updatesDirectory;
    }

    public async Task<ShellUpdate?> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!force && !settings.ShellUpdateCheckEnabled) return null;
        if (!force && settings.LastShellUpdateCheckUtc is { } lastCheck && DateTimeOffset.UtcNow - lastCheck < AutomaticCheckInterval) return null;

        var release = await _releaseClient.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        settings = settings with { LastShellUpdateCheckUtc = DateTimeOffset.UtcNow };
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        if (release is null) return null;

        var available = release.TagName.StartsWith('v') || release.TagName.StartsWith('V')
            ? release.TagName[1..]
            : release.TagName;
        if (!TryParseStrict(available, out var availableVersion) ||
            !TryParseStrict(_currentShellVersion, out var currentVersion))
        {
            return null;
        }

        if ((settings.IgnoredShellVersions ?? Array.Empty<string>()).Contains(available, StringComparer.OrdinalIgnoreCase)) return null;
        if (availableVersion <= currentVersion) return null;

        var setupAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, SetupAssetFileName, StringComparison.OrdinalIgnoreCase));
        var sumsAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, Sha256SumsAssetFileName, StringComparison.OrdinalIgnoreCase));
        if (setupAsset is null || sumsAsset is null) return null;

        return new ShellUpdate(_currentShellVersion, available, setupAsset.BrowserDownloadUrl, sumsAsset.BrowserDownloadUrl, setupAsset.Size);
    }

    public async Task<StagedShellUpdate> DownloadAndStageAsync(ShellUpdate update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_updatesDirectory);
        var expectedSha256 = ParseSetupSha256(await DownloadTextAsync(update.Sha256SumsDownloadUrl, cancellationToken).ConfigureAwait(false));
        var partialPath = Path.Combine(_updatesDirectory, $"{SetupAssetFileName}.{Guid.NewGuid():N}.partial");
        var finalPath = Path.Combine(_updatesDirectory, SetupAssetFileName);
        try
        {
            string actualSha256;
            await using (var fileStream = new FileStream(
                partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous))
            {
                actualSha256 = await DownloadSetupAsync(fileStream, update, progress, cancellationToken).ConfigureAwait(false);
            }

            if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"SHA-256 mismatch for {SetupAssetFileName}: expected {expectedSha256}, got {actualSha256}.");
            }

            File.Move(partialPath, finalPath, overwrite: true);
            var staged = new StagedShellUpdate(update.AvailableVersion, finalPath, expectedSha256, DateTimeOffset.UtcNow);
            await StagedShellUpdateStore.SaveAsync(staged, _updatesDirectory, cancellationToken).ConfigureAwait(false);
            return staged;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public async Task<StagedShellUpdate?> GetStagedUpdateAsync(CancellationToken cancellationToken = default)
    {
        var staged = await StagedShellUpdateStore.LoadAsync(_updatesDirectory, cancellationToken).ConfigureAwait(false);
        return staged is not null && File.Exists(staged.SetupPath) ? staged : null;
    }

    public void ClearStagedUpdate()
    {
        StagedShellUpdateStore.Clear(_updatesDirectory);
        TryDelete(Path.Combine(_updatesDirectory, SetupAssetFileName));
    }

    private async Task<string> DownloadSetupAsync(
        FileStream target,
        ShellUpdate update,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _downloadClient.GetAsync(update.SetupDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var totalBytes = update.SetupSizeBytes > 0
            ? update.SetupSizeBytes
            : response.Content.Headers.ContentLength ?? -1;
        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var sha256 = SHA256.Create();
        var buffer = new byte[81920];
        long receivedBytes = 0;
        int lastReportedPercent = -1;
        int read;
        while ((read = await downloadStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            sha256.TransformBlock(buffer, 0, read, null, 0);
            receivedBytes += read;
            if (progress is not null && totalBytes > 0)
            {
                var percent = (int)Math.Clamp(receivedBytes * 100 / totalBytes, 0, 100);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    progress.Report(percent);
                }
            }
        }

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private async Task<string> DownloadTextAsync(Uri downloadUrl, CancellationToken cancellationToken)
    {
        using var response = await _downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ParseSetupSha256(string sha256Sums)
    {
        foreach (var rawLine in sha256Sums.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (!line.EndsWith(SetupAssetFileName, StringComparison.OrdinalIgnoreCase)) continue;

            var hash = line.Split(' ', 2)[0].Trim();
            if (hash.Length == 64 && hash.All(char.IsAsciiHexDigit)) return hash.ToLowerInvariant();
        }

        throw new InvalidDataException($"SHA256SUMS.txt does not contain a valid entry for {SetupAssetFileName}.");
    }

    private static bool TryParseStrict(string text, out SemVersion version)
    {
        try
        {
            version = SemVersion.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            version = default;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
