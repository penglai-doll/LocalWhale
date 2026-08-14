namespace LocalWhale.Core.Runtime;

public sealed class HarnessStartupException : InvalidOperationException
{
    public const int MaximumCapturedLines = 256;

    public HarnessStartupException(int exitCode, IReadOnlyList<string> output)
        : base($"Harness exited with code {exitCode} before becoming ready.")
    {
        ArgumentNullException.ThrowIfNull(output);
        ExitCode = exitCode;
        Output = output;
    }

    public int ExitCode { get; }
    public IReadOnlyList<string> Output { get; }
}
