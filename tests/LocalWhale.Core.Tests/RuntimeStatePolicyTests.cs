using LocalWhale.Core.Models;
using LocalWhale.Core.Runtime;

namespace LocalWhale.Core.Tests;

public sealed class RuntimeStatePolicyTests
{
    [Fact]
    public void RegisterFailedStart_rolls_back_after_second_failure()
    {
        var initial = new RuntimeState("0.2.0-rc.1", "0.1.0-rc.6", new Dictionary<string, int>());

        var first = RuntimeStatePolicy.RegisterFailedStart(initial, "0.2.0-rc.1");
        var second = RuntimeStatePolicy.RegisterFailedStart(first.State, "0.2.0-rc.1");

        Assert.False(first.RolledBack);
        Assert.True(second.RolledBack);
        Assert.Equal("0.1.0-rc.6", second.State.ActiveVersion);
        Assert.Equal("0.2.0-rc.1", second.State.PreviousVersion);
        Assert.Equal(0, second.State.FailedStarts["0.1.0-rc.6"]);
    }

    [Fact]
    public void RegisterHealthyStart_clears_failure_count_for_active_version()
    {
        var initial = new RuntimeState("0.1.0-rc.6", null, new Dictionary<string, int> { ["0.1.0-rc.6"] = 1 });

        var updated = RuntimeStatePolicy.RegisterHealthyStart(initial);

        Assert.Equal(0, updated.FailedStarts["0.1.0-rc.6"]);
    }

    [Fact]
    public void RegisterUnexpectedExit_counts_only_exits_inside_the_sixty_second_window()
    {
        var initial = new RuntimeState("0.2.0", "0.1.0-rc.6", new Dictionary<string, int>());

        var early = RuntimeStatePolicy.RegisterUnexpectedExit(initial, "0.2.0", TimeSpan.FromSeconds(12));
        var stable = RuntimeStatePolicy.RegisterUnexpectedExit(early.State, "0.2.0", TimeSpan.FromSeconds(61));
        var secondEarly = RuntimeStatePolicy.RegisterUnexpectedExit(stable.State, "0.2.0", TimeSpan.FromSeconds(8));

        Assert.False(early.RolledBack);
        Assert.Equal(1, stable.State.FailedStarts["0.2.0"]);
        Assert.True(secondEarly.RolledBack);
        Assert.Equal("0.1.0-rc.6", secondEarly.State.ActiveVersion);
    }
}
