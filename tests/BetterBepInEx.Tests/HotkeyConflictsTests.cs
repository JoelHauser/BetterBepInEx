using BetterBepInEx.Core;

namespace BetterBepInEx.Tests;

public class HotkeyConflictsTests
{
    // Unity KeyCode values, so the cases read as real binds.
    private const int H = 104;
    private const int F12 = 293;
    private const int LeftShift = 304;
    private const int LeftControl = 306;
    private const int LeftAlt = 308;

    private static HotkeyConflicts.Binding<string> Bind(string owner, int main, params int[] mods) =>
        new() { Owner = owner, MainKey = main, Modifiers = mods };

    [Fact]
    public void TheSameKeyTwiceIsAConflict()
    {
        var groups = HotkeyConflicts.Find(new[] { Bind("a", H), Bind("b", H), Bind("c", F12) });
        var group = Assert.Single(groups);
        Assert.Equal(new[] { "a", "b" }, group.Select(b => b.Owner));
    }

    [Fact]
    public void DifferentModifiersAreDifferentBinds()
    {
        // BepInEx's IsDown wants the exact modifier set, so H and Ctrl+H never fire together.
        Assert.Empty(HotkeyConflicts.Find(new[] { Bind("a", H), Bind("b", H, LeftControl) }));
    }

    [Fact]
    public void ModifierOrderDoesNotMatter()
    {
        var groups = HotkeyConflicts.Find(new[]
        {
            Bind("a", H, LeftControl, LeftShift),
            Bind("b", H, LeftShift, LeftControl),
        });
        Assert.Equal(2, Assert.Single(groups).Count);
    }

    [Fact]
    public void UnboundSettingsNeverConflict()
    {
        Assert.Empty(HotkeyConflicts.Find(new[] { Bind("a", 0), Bind("b", 0) }));
    }

    [Fact]
    public void ARepeatedOrSelfModifierIsIgnored()
    {
        Assert.Equal(HotkeyConflicts.ComboKey(H, new[] { LeftAlt }), HotkeyConflicts.ComboKey(H, new[] { LeftAlt, LeftAlt, H }));
    }

    [Fact]
    public void LargestGroupComesFirst()
    {
        var groups = HotkeyConflicts.Find(new[]
        {
            Bind("a", H), Bind("b", H),
            Bind("c", F12), Bind("d", F12), Bind("e", F12),
        });
        Assert.Equal(3, groups[0].Count);
        Assert.Equal(2, groups[1].Count);
    }
}
