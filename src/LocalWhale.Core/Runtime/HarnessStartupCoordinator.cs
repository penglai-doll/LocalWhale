using LocalWhale.Core.Models;

namespace LocalWhale.Core.Runtime;

public static class HarnessStartupCoordinator
{
    public static async Task<HarnessRuntimeInfo> StartAsync(
        IHarnessRuntimeManager manager,
        string version,
        string dshHomeDirectory,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(dshHomeDirectory);

        try
        {
            return await manager.StartAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (HarnessStartupException exception)
            when (HarnessStartupRetryPolicy.ShouldRetryProfileResolution(exception, dshHomeDirectory))
        {
            log?.Invoke("Harness profile dependencies were linked during startup; retrying once in a fresh process.");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            return await manager.StartAsync(version, cancellationToken).ConfigureAwait(false);
        }
    }
}
