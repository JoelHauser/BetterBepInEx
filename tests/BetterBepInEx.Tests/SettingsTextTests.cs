using BetterBepInEx.Core;

namespace BetterBepInEx.Tests;

public class SettingsTextTests
{
    private static SettingsText.Record R(string guid, string section, string key, string value) =>
        new() { Guid = guid, Section = section, Key = key, Value = value };

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        var records = new[]
        {
            R("com.a.mod", "General", "Enabled", "true"),
            R("com.b.mod", "Keys", "Open", "LeftControl + H"),
            R("com.b.mod", "", "Empty section", ""),
        };
        string text = SettingsText.Write(records, "two mods");
        Assert.True(SettingsText.LooksLikeSettings(text));

        var read = SettingsText.Read(text, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal(3, read.Count);
        Assert.Equal("LeftControl + H", read[1].Value);
        Assert.Equal("", read[2].Section);
        Assert.Equal("", read[2].Value);
        Assert.Equal(SettingsText.MakeId("com.b.mod", "Keys", "Open"), read[1].Id);
    }

    [Fact]
    public void ToleratesTextThatWentThroughChat()
    {
        string text = SettingsText.Magic + "\r\n\r\n  com.a.mod\tGeneral\tScale\t1.5  \r\n# a comment\r\n";
        var read = SettingsText.Read(text, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal("1.5", Assert.Single(read).Value);
    }

    [Fact]
    public void ATabInsideTheValueSurvives()
    {
        var read = SettingsText.Read("com.a.mod\tGeneral\tName\tleft\tright", out _);
        Assert.Equal("left\tright", Assert.Single(read).Value);
    }

    [Fact]
    public void ShortOrKeylessLinesAreCountedAndSkipped()
    {
        var read = SettingsText.Read("com.a.mod\tGeneral\n\tGeneral\tKey\tv\ncom.a.mod\tGeneral\t\tv\nok\ts\tk\tv", out int malformed);
        Assert.Equal(3, malformed);
        Assert.Equal("ok", Assert.Single(read).Guid);
    }

    [Fact]
    public void PlainClipboardTextIsNotSettings()
    {
        Assert.False(SettingsText.LooksLikeSettings("hello"));
        Assert.False(SettingsText.LooksLikeSettings(null));
        Assert.False(SettingsText.LooksLikeSettings(""));
    }
}
