using F12Overhaul.Core;

namespace F12Overhaul.Tests;

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
