using LocalWhale.Core.Models;

namespace LocalWhale.Core.Runtime;

public sealed record RuntimeStateTransition(RuntimeState State, bool RolledBack);

public static class RuntimeStatePolicy
{
    public const int FailureThreshold = 2;

    public static RuntimeStateTransition RegisterFailedStart(RuntimeState state, string version)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var failures = new Dictionary<string, int>(state.FailedStarts, StringComparer.OrdinalIgnoreCase);
        failures[version] = failures.GetValueOrDefault(version) + 1;

        if (!string.Equals(state.ActiveVersion, version, StringComparison.OrdinalIgnoreCase) ||
            failures[version] < FailureThreshold ||
            string.IsNullOrWhiteSpace(state.PreviousVersion))
        {
            return new RuntimeStateTransition(state with { FailedStarts = failures }, false);
        }

        var rollbackVersion = state.PreviousVersion;
        failures[rollbackVersion] = 0;
        return new RuntimeStateTransition(
            new RuntimeState(rollbackVersion, state.ActiveVersion, failures),
            true);
    }

    public static RuntimeStateTransition RegisterUnexpectedExit(RuntimeState state, string version, TimeSpan uptime)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (uptime >= TimeSpan.FromSeconds(60)) return new RuntimeStateTransition(state, false);
        return RegisterFailedStart(state, version);
    }

    public static RuntimeState RegisterHealthyStart(RuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var failures = new Dictionary<string, int>(state.FailedStarts, StringComparer.OrdinalIgnoreCase)
        {
            [state.ActiveVersion] = 0
        };
        return state with { FailedStarts = failures };
    }
}
