using LocalWhale.Core.Models;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Updates;

/// <summary>
/// Reads and writes the staged shell-update marker in the updates directory.
/// The marker is the contract between the download flow and the next-launch installer.
/// </summary>
public static class StagedShellUpdateStore
{
    public const string MarkerFileName = "staged-shell-update.json";

    public static string MarkerPath(string updatesDirectory) => Path.Combine(updatesDirectory, MarkerFileName);

    public static async Task<StagedShellUpdate?> LoadAsync(string updatesDirectory, CancellationToken cancellationToken = default)
    {
        var file = new AtomicJsonFile<StagedShellUpdate>(MarkerPath(updatesDirectory));
        return await file.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public static Task SaveAsync(StagedShellUpdate staged, string updatesDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(updatesDirectory);
        return new AtomicJsonFile<StagedShellUpdate>(MarkerPath(updatesDirectory)).SaveAsync(staged, cancellationToken);
    }

    public static void Clear(string updatesDirectory)
    {
        try
        {
            File.Delete(MarkerPath(updatesDirectory));
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was ever staged.
        }
    }
}
