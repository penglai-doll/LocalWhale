using System.Diagnostics;

namespace LocalWhale.Core.Runtime;

public sealed record HarnessLaunchSpec(
    string NodeExecutable,
    string DshBinPath,
    string PatchPath,
    string WorkingDirectory,
    string BridgeToken,
    string? DshHome = null,
    string? HarnessVersion = null,
    string? BridgeModulePath = null);

public static class HarnessCommandBuilder
{
    public static ProcessStartInfo Build(HarnessLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var info = new ProcessStartInfo
        {
            FileName = spec.NodeExecutable,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // dsh web enables command pass-through; host/port belong to the inner app,
        // so the outer --patch option must appear before the first inner argument.
        foreach (var argument in new[] { spec.DshBinPath, "web", "--patch", spec.PatchPath, "--host", "127.0.0.1", "--port", "0" })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment[HarnessEnvironment.BridgeToken] = spec.BridgeToken;
        if (!string.IsNullOrWhiteSpace(spec.DshHome)) info.Environment["DSH_HOME"] = spec.DshHome;
        if (!string.IsNullOrWhiteSpace(spec.HarnessVersion)) info.Environment[HarnessEnvironment.HarnessVersion] = spec.HarnessVersion;
        return info;
    }
}
