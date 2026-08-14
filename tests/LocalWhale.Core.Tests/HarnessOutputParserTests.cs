using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class HarnessOutputParserTests
{
    [Theory]
    [InlineData("dsh web: http://127.0.0.1:49152", "http://127.0.0.1:49152/")]
    [InlineData("dsh web: http://127.0.0.1:49152 (LAN: http://192.168.1.3:49152)", "http://127.0.0.1:49152/")]
    public void TryParseReadyUri_extracts_only_the_canonical_loopback_url(string line, string expected)
    {
        Assert.True(HarnessOutputParser.TryParseReadyUri(line, out var uri));
        Assert.Equal(new Uri(expected), uri);
    }

    [Theory]
    [InlineData("dsh web: http://0.0.0.0:3080")]
    [InlineData("unrelated log line")]
    public void TryParseReadyUri_rejects_non_loopback_and_unrelated_lines(string line)
    {
        Assert.False(HarnessOutputParser.TryParseReadyUri(line, out _));
    }
}
