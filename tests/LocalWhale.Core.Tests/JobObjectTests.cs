using System.Diagnostics;
using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class JobObjectTests
{
    [Fact]
    public async Task Dispose_terminates_an_assigned_process_tree()
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" }
        }) ?? throw new InvalidOperationException("Failed to start fixture process.");
        var job = new ProcessJob();
        job.Assign(process);

        job.Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        Assert.True(process.HasExited);
    }
}
