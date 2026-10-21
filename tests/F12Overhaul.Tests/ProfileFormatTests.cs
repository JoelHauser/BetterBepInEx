using F12Overhaul.Core;

namespace F12Overhaul.Tests;

public class ProfileFormatTests
{
    private static ProfileFormat.Record R(string guid, string section, string key, string value) =>
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
        string text = ProfileFormat.Write(records, "two mods");
        Assert.True(ProfileFormat.LooksLikeSettings(text));

        var read = ProfileFormat.Read(text, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal(3, read.Count);
        Assert.Equal("LeftControl + H", read[1].Value);
        Assert.Equal("", read[2].Section);
        Assert.Equal("", read[2].Value);
        Assert.Equal(ProfileFormat.MakeId("com.b.mod", "Keys", "Open"), read[1].Id);
    }

    [Fact]
    public void ToleratesTextThatWentThroughChat()
    {
        string text = ProfileFormat.Magic + "\r\n\r\n  com.a.mod\tGeneral\tScale\t1.5  \r\n# a comment\r\n";
        var read = ProfileFormat.Read(text, out int malformed);
        Assert.Equal(0, malformed);
        Assert.Equal("1.5", Assert.Single(read).Value);
    }

    [Fact]
    public void ATabInsideTheValueSurvives()
    {
        var read = ProfileFormat.Read("com.a.mod\tGeneral\tName\tleft\tright", out _);
        Assert.Equal("left\tright", Assert.Single(read).Value);
    }

    [Fact]
    public void ShortOrKeylessLinesAreCountedAndSkipped()
    {
        var read = ProfileFormat.Read("com.a.mod\tGeneral\n\tGeneral\tKey\tv\ncom.a.mod\tGeneral\t\tv\nok\ts\tk\tv", out int malformed);
        Assert.Equal(3, malformed);
        Assert.Equal("ok", Assert.Single(read).Guid);
    }

    [Fact]
    public void PlainClipboardTextIsNotSettings()
    {
        Assert.False(ProfileFormat.LooksLikeSettings("hello"));
        Assert.False(ProfileFormat.LooksLikeSettings(null));
        Assert.False(ProfileFormat.LooksLikeSettings(""));
    }

    [Theory]
    [InlineData("streaming", "streaming")]
    [InlineData("  Fika night  ", "Fika night")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("..\\..\\evil", "_._.._evil")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public void SafeFileNameStaysInsideTheFolder(string name, string? expected)
    {
        string? safe = ProfileFormat.SafeFileName(name);
        Assert.Equal(expected, safe);
        if (safe != null)
        {
            Assert.DoesNotContain('/', safe);
            Assert.DoesNotContain('\\', safe);
            Assert.False(safe.StartsWith('.'));
        }
    }

    [Fact]
    public void SafeFileNameIsCapped()
    {
        Assert.Equal(60, ProfileFormat.SafeFileName(new string('x', 200))!.Length);
    }
}
