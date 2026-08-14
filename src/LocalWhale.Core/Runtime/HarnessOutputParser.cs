using System.Text.RegularExpressions;

namespace LocalWhale.Core.Runtime;

public static partial class HarnessOutputParser
{
    public static bool TryParseReadyUri(string? line, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var match = ReadyLineRegex().Match(line);
        if (!match.Success || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var candidate)) return false;
        if (!candidate.IsLoopback || !string.Equals(candidate.Host, "127.0.0.1", StringComparison.Ordinal)) return false;
        uri = candidate;
        return true;
    }

    [GeneratedRegex("^dsh web:\\s+(http://127\\.0\\.0\\.1:[0-9]{1,5})(?:\\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ReadyLineRegex();
}
