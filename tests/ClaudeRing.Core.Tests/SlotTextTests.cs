namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class SlotTextTests
{
    [Fact]
    public void Task_key_is_split() => Assert.Equal(("PROJ", "1069"), SlotText.Format("PROJ-1069"));

    [Fact]
    public void Plain_label_stays_whole() => Assert.Equal((null, "Чат в VS Code"), SlotText.Format("Чат в VS Code"));

    [Fact]
    public void Empty_label() => Assert.Equal((null, ""), SlotText.Format(""));
}
