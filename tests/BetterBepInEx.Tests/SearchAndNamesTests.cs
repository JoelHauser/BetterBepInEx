using BetterBepInEx.Core;

namespace BetterBepInEx.Tests;

public class SearchAndNamesTests
{
    private static readonly string Hay = SearchQuery.Haystack("Inventory Walker", "com.mybutthasarash.inventorywalker",
        "mybutthasarash", "General", "Loot range in metres", "When looting a bag, walking further closes the view.");

    [Theory]
    [InlineData("", true)]
    [InlineData("loot", true)]
    [InlineData("LOOT RANGE", true)]
    [InlineData("range walker", true)]
    [InlineData("metres bag", true)]
    [InlineData("loot fov", false)]
    [InlineData("mybutthasarash", true)]
    public void EveryWordHasToMatchSomewhere(string query, bool expected)
    {
        Assert.Equal(expected, new SearchQuery(query).Matches(Hay));
    }

    [Fact]
    public void ExtraSpacesAreNotWords()
    {
        var q = new SearchQuery("   loot    range  ");
        Assert.False(q.IsEmpty);
        Assert.True(q.Matches(Hay));
        Assert.True(new SearchQuery("   ").IsEmpty);
    }

    [Theory]
    [InlineData("loo", "loot", true)]
    [InlineData("loot", "loot range", true)]
    [InlineData("ran loo", "loot range", true)]
    [InlineData("loot range", "lootrange", true)]
    [InlineData("range loot", "loot", false)]
    [InlineData("loot", "loo", false)]
    [InlineData("loot range", "range", false)]
    [InlineData("", "loot", false)]
    public void NarrowsWhenEveryEarlierWordIsKept(string previous, string next, bool expected)
    {
        Assert.Equal(expected, new SearchQuery(next).Narrows(new SearchQuery(previous)));
    }

    [Fact]
    public void ANarrowerSearchOnlyMatchesWhatTheEarlierOneDid()
    {
        string[] hays =
        {
            Hay,
            SearchQuery.Haystack("Loot Radius", "General", "Range of the loot ring"),
            SearchQuery.Haystack("Ranger", "Other", "nothing to see"),
        };
        var earlier = new SearchQuery("ran loo");
        var later = new SearchQuery("loot range");
        Assert.True(later.Narrows(earlier));
        foreach (string hay in hays)
        {
            Assert.True(!later.Matches(hay) || earlier.Matches(hay));
        }
    }

    [Theory]
    [InlineData("!Important Mod", "Important Mod")]
    [InlineData("  Padded ", "Padded")]
    [InlineData("", "com.x.y")]
    [InlineData(null, "com.x.y")]
    public void CleanDropsBangsAndFallsBackToTheGuid(string? name, string expected)
    {
        Assert.Equal(expected, DisplayNames.Clean(name!, "com.x.y"));
    }

    [Theory]
    [InlineData("SPT-Casino", "casino")]
    [InlineData("SPT Realism", "realism")]
    [InlineData("SPTarkov Thing", "sptarkov thing")]
    [InlineData("[Fika] Core", "fika] core")]
    [InlineData("Amands Graphics", "amands graphics")]
    public void SortKeyIgnoresTheSptPrefix(string name, string expected)
    {
        Assert.Equal(expected, DisplayNames.SortKey(name));
    }

    [Theory]
    [InlineData("com.acidphantasm.bepinexconfigurationmanager", "acidphantasm")]
    [InlineData("com.bepis.bepinex.configurationmanager", "bepis")]
    [InlineData("BepInEx", null)]
    [InlineData("Tyfon.UIFixes", null)]
    [InlineData("xyz.drakia.waypoints", "drakia")]
    [InlineData("", null)]
    public void AuthorComesFromReverseDomainGuids(string guid, string? expected)
    {
        Assert.Equal(expected, DisplayNames.AuthorFromGuid(guid));
    }
}

public class PrettyNameTests
{
    [Theory]
    [InlineData("Tyfon.UIFixes", "UI Fixes")]
    [InlineData("7Bpencil.WeaponCamoAndStickers", "Weapon Camo And Stickers")]
    [InlineData("7Bpencil.BrighterInteriors", "Brighter Interiors")]
    [InlineData("dvize.GTFO", "GTFO")]
    [InlineData("Fontaine-SPTStanceOverhaul", "SPT Stance Overhaul")]
    [InlineData("BlackHawk-QuickSell", "Quick Sell")]
    [InlineData("acidphantasm-brightlasers", "acidphantasm-brightlasers")]
    [InlineData("AOSix", "AOSix")]
    [InlineData("Amands's Graphics", "Amands's Graphics")]
    [InlineData("BarterItemsStacksClient", "BarterItemsStacksClient")]
    [InlineData("Some.dll", "Some.dll")]
    public void CodeNamesBecomeReadable(string name, string expected)
    {
        Assert.Equal(expected, DisplayNames.Pretty(name, "com.x.y"));
    }

    [Theory]
    [InlineData("Tyfon.UIFixes", "Tyfon")]
    [InlineData("Fontaine-SPTStanceOverhaul", "Fontaine")]
    [InlineData("AOSix", null)]
    [InlineData("acidphantasm-brightlasers", null)]
    public void TheDroppedPrefixIsTheAuthor(string name, string? expected)
    {
        Assert.Equal(expected, DisplayNames.AuthorFromName(name));
    }

    [Theory]
    [InlineData("PageUp", "Page Up")]
    [InlineData("UIFixes", "UI Fixes")]
    [InlineData("F12", "F12")]
    [InlineData("KeypadEnter", "Keypad Enter")]
    public void SplitWordsSpacesCamelCase(string input, string expected)
    {
        Assert.Equal(expected, DisplayNames.SplitWords(input));
    }
}

public class ScreenScaleTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0f)]
    [InlineData(2560, 1440, 1.3333f)]
    [InlineData(3440, 1440, 1.3333f)]   // 21:9 is a wider 1440p, not a bigger one
    [InlineData(5120, 1440, 1.3333f)]   // 32:9 likewise
    [InlineData(3840, 2160, 2.0f)]
    [InlineData(1920, 1200, 1.0f)]      // 16:10: width decides, so it still fits across
    [InlineData(1280, 1024, 0.6667f)]   // 5:4
    [InlineData(1280, 720, 0.6667f)]
    [InlineData(800, 600, 0.5f)]        // floored, so text stays readable
    public void ScalesWithHeightUnlessTheScreenIsNarrow(int w, int h, float expected)
    {
        Assert.Equal(expected, BetterBepInEx.Core.ScreenScale.For(w, h), 3);
    }

    [Fact]
    public void TheUserScaleMultipliesAndIsClamped()
    {
        Assert.Equal(2.0f, BetterBepInEx.Core.ScreenScale.Effective(2560, 1440, 1.5f, true), 3);
        Assert.Equal(1.5f, BetterBepInEx.Core.ScreenScale.Effective(2560, 1440, 1.5f, false), 3);
        Assert.Equal(4.0f, BetterBepInEx.Core.ScreenScale.Effective(7680, 4320, 2f, true), 3);
    }

    [Fact]
    public void ABadScreenSizeDoesNotScale()
    {
        Assert.Equal(1f, BetterBepInEx.Core.ScreenScale.For(0, 0));
    }
}
