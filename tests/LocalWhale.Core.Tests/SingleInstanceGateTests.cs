using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class SingleInstanceGateTests
{
    [Fact]
    public void Only_first_gate_with_same_name_is_primary()
    {
        var name = $"LocalWhale.Tests.{Guid.NewGuid():N}";

        using var first = new SingleInstanceGate(name);
        using var second = new SingleInstanceGate(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
    }
}
