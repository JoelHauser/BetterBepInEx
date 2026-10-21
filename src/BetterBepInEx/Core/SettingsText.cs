using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// The text format for settings copied to the clipboard. One setting per line, tab separated:
    /// plugin GUID, section, key, value. Lines starting with '#' are comments.
    ///
    /// Tabs are safe as the separator. BepInEx refuses tabs in section and key names, and its
    /// serializer escapes tabs and newlines inside string values, so a value is always one line.
    /// The value is the last field and is split off with a limit, so a stray tab in it survives.
    /// </summary>
    public static class SettingsText
    {
        public const string Magic = "# BetterBepInEx settings";

        public sealed class Record
        {
            public string Guid;
            public string Section;
            public string Key;
            public string Value;

            public string Id => MakeId(Guid, Section, Key);
        }

        public static string MakeId(string guid, string section, string key)
        {
            return guid + "\t" + section + "\t" + key;
        }

        public static string Write(IEnumerable<Record> records, string description)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Magic).Append('\n');
            if (!string.IsNullOrEmpty(description))
            {
                foreach (string line in description.Replace("\r", "").Split('\n'))
                {
                    sb.Append("# ").Append(line).Append('\n');
                }
            }
            foreach (Record r in records)
            {
                sb.Append(r.Guid).Append('\t')
                  .Append(r.Section).Append('\t')
                  .Append(r.Key).Append('\t')
                  .Append(r.Value ?? string.Empty).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reads records from text, which may have come off a clipboard and been pasted through chat,
        /// so it tolerates CRLF, blank lines, and leading or trailing spaces on the line. Lines with
        /// fewer than four fields are counted in <paramref name="malformed"/> and skipped.
        /// </summary>
        public static List<Record> Read(string text, out int malformed)
        {
            malformed = 0;
            List<Record> records = new List<Record>();
            if (string.IsNullOrEmpty(text))
            {
                return records;
            }

            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim(' ', '\r');
                    if (trimmed.Length == 0 || trimmed[0] == '#')
                    {
                        continue;
                    }
                    string[] parts = trimmed.Split(new[] { '\t' }, 4);
                    if (parts.Length < 4 || parts[0].Length == 0 || parts[2].Length == 0)
                    {
                        malformed++;
                        continue;
                    }
                    records.Add(new Record { Guid = parts[0], Section = parts[1], Key = parts[2], Value = parts[3] });
                }
            }
            return records;
        }

        /// <summary>True when the text starts with this format's header line.</summary>
        public static bool LooksLikeSettings(string text)
        {
            return text != null && text.TrimStart().StartsWith(Magic, StringComparison.Ordinal);
        }
    }
}
