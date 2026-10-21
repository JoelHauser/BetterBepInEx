using System.Collections.Generic;
using System.Text;

namespace F12Overhaul.Core
{
    /// <summary>
    /// Finds keybinds that two or more settings share. Works on plain key codes (Unity's KeyCode as
    /// an int) so the tests need no Unity.
    ///
    /// BepInEx's KeyboardShortcut.IsDown requires the exact modifier set to be held, so Ctrl+H and
    /// H are different binds and do not conflict. Modifier order does not matter.
    /// </summary>
    public static class HotkeyConflicts
    {
        public sealed class Binding<TOwner>
        {
            public TOwner Owner;
            public int MainKey;
            public int[] Modifiers;
        }

        /// <summary>
        /// Returns the groups of bindings that share a combination, largest group first. Unbound
        /// settings (main key 0, KeyCode.None) are skipped.
        /// </summary>
        public static List<List<Binding<TOwner>>> Find<TOwner>(IEnumerable<Binding<TOwner>> bindings)
        {
            Dictionary<string, List<Binding<TOwner>>> byCombo = new Dictionary<string, List<Binding<TOwner>>>();
            List<string> order = new List<string>();
            foreach (Binding<TOwner> binding in bindings)
            {
                if (binding == null || binding.MainKey == 0)
                {
                    continue;
                }
                string combo = ComboKey(binding.MainKey, binding.Modifiers);
                if (!byCombo.TryGetValue(combo, out List<Binding<TOwner>> list))
                {
                    list = new List<Binding<TOwner>>();
                    byCombo[combo] = list;
                    order.Add(combo);
                }
                list.Add(binding);
            }

            List<List<Binding<TOwner>>> result = new List<List<Binding<TOwner>>>();
            foreach (string combo in order)
            {
                if (byCombo[combo].Count > 1)
                {
                    result.Add(byCombo[combo]);
                }
            }
            result.Sort((a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        /// <summary>A stable string for a combination: main key, then the distinct modifiers sorted.</summary>
        public static string ComboKey(int mainKey, int[] modifiers)
        {
            List<int> mods = new List<int>();
            if (modifiers != null)
            {
                foreach (int m in modifiers)
                {
                    if (m != 0 && m != mainKey && !mods.Contains(m))
                    {
                        mods.Add(m);
                    }
                }
            }
            mods.Sort();

            StringBuilder sb = new StringBuilder();
            sb.Append(mainKey);
            foreach (int m in mods)
            {
                sb.Append('+').Append(m);
            }
            return sb.ToString();
        }
    }
}
