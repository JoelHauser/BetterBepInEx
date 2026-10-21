using System.Text.RegularExpressions;

namespace BetterBepInEx.Tests;

/// <summary>
/// BepInEx throws on these characters in a section or key name, and the throw happens inside the
/// plugin's Awake, where it is logged only to Player.log and the plugin silently does nothing.
/// Copied from SPerformanceT, which shipped an '=' in a key once. Reads every Config.Bind call in
/// the plugin's source.
/// </summary>
public class ConfigKeyTests
{
    private static readonly char[] Forbidden = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

    private static readonly Regex Bind = new(
        @"\.Bind\(\s*""(?<section>[^""]*)""\s*,\s*""(?<key>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    private static string SourceRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "BetterBepInEx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "BetterBepInEx");
    }

    public static IEnumerable<object[]> Keys()
    {
        foreach (string file in Directory.GetFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in Bind.Matches(text))
            {
                yield return new object[] { Path.GetFileName(file), m.Groups["section"].Value, Regex.Unescape(m.Groups["key"].Value) };
            }
        }
    }

    [Fact]
    public void ThePluginBindsItsKeys()
    {
        // Guards the test itself: if the regex stops matching, the theory below would pass on nothing.
        Assert.True(Keys().Count() >= 10, "found only " + Keys().Count() + " Config.Bind calls");
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ConfigNamesUseOnlyAllowedCharacters(string file, string section, string key)
    {
        Assert.False(string.IsNullOrEmpty(section), file + ": no section for '" + key + "'");
        Assert.True(section.IndexOfAny(Forbidden) < 0, file + ": section '" + section + "'");
        Assert.True(key.IndexOfAny(Forbidden) < 0, file + ": key '" + key + "'");
    }
}
