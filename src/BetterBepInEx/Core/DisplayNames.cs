using System;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// Mod names as the sidebar shows and sorts them. BepInEx plugin names are whatever the author
    /// typed: some start with '!' to sort first in the stock menu, many start with "SPT-", and some
    /// are blank. The display name drops the '!'; the sort key also drops a leading "SPT", so the
    /// list is not one long run of S.
    /// </summary>
    public static class DisplayNames
    {
        public static string Clean(string name, string guid)
        {
            string trimmed = (name ?? string.Empty).TrimStart('!').Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
            return string.IsNullOrEmpty(guid) ? "(unnamed mod)" : guid;
        }

        /// <summary>
        /// A readable name for mods that use a code name as their display name. "Tyfon.UIFixes"
        /// becomes "UI Fixes" and "Fontaine-SPTStanceOverhaul" becomes "SPT Stance Overhaul": the
        /// part after the last '.' (or the last '-', when that part has capitals), with spaces
        /// put between its words. Names that already contain a space are left alone, as are
        /// all-lowercase ones like "acidphantasm-brightlasers", where there is nothing to split on.
        /// </summary>
        public static string Pretty(string name, string guid)
        {
            string clean = Clean(name, guid);
            if (clean.IndexOf(' ') >= 0)
            {
                return clean;
            }
            string tail = null;
            int dot = clean.LastIndexOf('.');
            if (dot >= 0 && dot < clean.Length - 2)
            {
                tail = clean.Substring(dot + 1);
            }
            else
            {
                int dash = clean.LastIndexOf('-');
                if (dash > 0 && dash < clean.Length - 2 && HasUpper(clean.Substring(dash + 1)))
                {
                    tail = clean.Substring(dash + 1);
                }
            }
            if (tail == null || !HasUpper(tail))
            {
                return clean;
            }
            return SplitWords(tail);
        }

        /// <summary>The "author" part that <see cref="Pretty"/> dropped, or null when it dropped nothing.</summary>
        public static string AuthorFromName(string name)
        {
            string clean = (name ?? string.Empty).TrimStart('!').Trim();
            if (clean.Length == 0 || clean.IndexOf(' ') >= 0)
            {
                return null;
            }
            string pretty = Pretty(clean, null);
            if (pretty == clean)
            {
                return null;
            }
            int cut = clean.LastIndexOf('.') >= 0 && clean.LastIndexOf('.') < clean.Length - 2 ? clean.LastIndexOf('.') : clean.LastIndexOf('-');
            string author = cut > 0 ? clean.Substring(0, cut) : null;
            return string.IsNullOrEmpty(author) ? null : author;
        }

        /// <summary>"WeaponCamoAndStickers" to "Weapon Camo And Stickers", "UIFixes" to "UI Fixes", "GTFO" stays.</summary>
        public static string SplitWords(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    {
                        sb.Append(' ');
                    }
                    continue;
                }
                if (i > 0 && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    char prev = s[i - 1];
                    bool next = i + 1 < s.Length && char.IsLower(s[i + 1]);
                    if (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev) || char.IsUpper(prev) && next))
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static bool HasUpper(string s)
        {
            foreach (char c in s)
            {
                if (char.IsUpper(c))
                {
                    return true;
                }
            }
            return false;
        }

        public static string SortKey(string displayName)
        {
            string s = (displayName ?? string.Empty).Trim();
            if (s.Length > 3 && s.StartsWith("spt", StringComparison.OrdinalIgnoreCase))
            {
                char next = s[3];
                if (next == '-' || next == '_' || next == ' ' || next == '.' || next == ':')
                {
                    s = s.Substring(4);
                }
            }
            int start = 0;
            while (start < s.Length && !char.IsLetterOrDigit(s[start]))
            {
                start++;
            }
            return s.Substring(start).ToLowerInvariant();
        }

        /// <summary>
        /// The author part of a reverse-domain GUID such as "com.acidphantasm.something", or null
        /// when the GUID is not shaped like one.
        /// </summary>
        public static string AuthorFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }
            string[] parts = guid.Split('.');
            if (parts.Length < 3)
            {
                return null;
            }
            string tld = parts[0].ToLowerInvariant();
            if (tld != "com" && tld != "net" && tld != "org" && tld != "io" && tld != "dev"
                && tld != "xyz" && tld != "me" && tld != "github")
            {
                return null;
            }
            return parts[1].Length == 0 ? null : parts[1];
        }
    }
}
