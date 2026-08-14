using System.Diagnostics;
using LocalWhale.Core.Persistence;

namespace LocalWhale.Core.Updates;

public static class PnpmCommandBuilder
{
    public static ProcessStartInfo Build(LocalWhalePaths paths, string workingDirectory, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = paths.NodeExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(paths.PnpmScript);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var nodeDirectory = Path.GetDirectoryName(paths.NodeExecutable)
            ?? throw new InvalidOperationException("Bundled Node executable must have a parent directory.");
        startInfo.Environment["PATH"] = nodeDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["CI"] = "1";
        return startInfo;
    }
}
