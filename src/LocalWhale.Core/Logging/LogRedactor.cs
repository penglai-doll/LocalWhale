using System.Text.RegularExpressions;

namespace LocalWhale.Core.Logging;

public static partial class LogRedactor
{
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = BridgeTokenRegex().Replace(value, "$1[REDACTED]");
        result = SensitiveEnvironmentRegex().Replace(result, "$1[REDACTED]");
        return AuthorizationRegex().Replace(result, "$1[REDACTED]");
    }

    [GeneratedRegex("(?i)(X-LocalWhale-Token\\s*:\\s*)[^\\s]+")]
    private static partial Regex BridgeTokenRegex();

    [GeneratedRegex("(?i)((?:DEEPSEEK_API_KEY|OPENAI_API_KEY|ANTHROPIC_API_KEY|LOCALWHALE_BRIDGE_TOKEN)\\s*=\\s*)[^\\s]+")]
    private static partial Regex SensitiveEnvironmentRegex();

    [GeneratedRegex("(?i)(Authorization\\s*:\\s*(?:Bearer|Basic)\\s+)[^\\s]+")]
    private static partial Regex AuthorizationRegex();
}
