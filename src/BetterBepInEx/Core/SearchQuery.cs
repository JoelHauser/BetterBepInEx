using System;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// The search box. Every word typed has to appear somewhere in a setting's text (its mod name,
    /// GUID, section, name and description), in any order and any case, the same rule the stock
    /// menu uses. The haystack is lower-cased once when the list is built, not on every frame.
    /// </summary>
    public sealed class SearchQuery
    {
        public static readonly SearchQuery Empty = new SearchQuery(string.Empty);

        private readonly string[] _words;

        public string Text { get; }

        public bool IsEmpty => _words.Length == 0;

        public SearchQuery(string text)
        {
            Text = text ?? string.Empty;
            _words = Text.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public bool Matches(string lowerHaystack)
        {
            if (_words.Length == 0)
            {
                return true;
            }
            if (string.IsNullOrEmpty(lowerHaystack))
            {
                return false;
            }
            foreach (string word in _words)
            {
                if (lowerHaystack.IndexOf(word, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True when everything this query matches, <paramref name="previous"/> matched too: each of
        /// the earlier words is part of one of these. Typing more letters or words narrows a search,
        /// so its results can be filtered from the last ones instead of every setting.
        /// </summary>
        public bool Narrows(SearchQuery previous)
        {
            if (previous == null || previous.IsEmpty)
            {
                return false;
            }
            foreach (string old in previous._words)
            {
                bool found = false;
                foreach (string word in _words)
                {
                    if (word.IndexOf(old, StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            return true;
        }

        public static string Haystack(params string[] parts)
        {
            return string.Join("\n", parts).ToLowerInvariant();
        }
    }
}
