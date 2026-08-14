using LocalWhale.Core.Logging;

namespace LocalWhale.Core.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void Redact_removes_bridge_tokens_api_keys_and_sensitive_environment_values()
    {
        const string line = "X-LocalWhale-Token: bridge-secret DEEPSEEK_API_KEY=sk-live-123 Authorization: Bearer abc.def";

        var redacted = LogRedactor.Redact(line);

        Assert.DoesNotContain("bridge-secret", redacted);
        Assert.DoesNotContain("sk-live-123", redacted);
        Assert.DoesNotContain("abc.def", redacted);
        Assert.Equal("X-LocalWhale-Token: [REDACTED] DEEPSEEK_API_KEY=[REDACTED] Authorization: Bearer [REDACTED]", redacted);
    }
}
