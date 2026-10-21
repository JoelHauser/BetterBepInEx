using System.Collections.Generic;
using BetterBepInEx.Core;

namespace BetterBepInEx
{
    /// <summary>
    /// Copying settings to the clipboard as text, and applying text pasted back, in
    /// <see cref="SettingsText"/>. Used by Copy and Paste on a mod's page and on the Changed page.
    /// </summary>
    internal static class SettingsClipboard
    {
        public sealed class Result
        {
            public int Applied;
            public int Unchanged;
            public int Unknown;
            public int Failed;
            public int Malformed;
        }

        public static IEnumerable<SettingsText.Record> Records(IEnumerable<Row> rows)
        {
            foreach (Row row in rows)
            {
                if (row.Entry == null || row.Id == null)
                {
                    continue;
                }
                yield return new SettingsText.Record
                {
                    Guid = row.Page.Guid,
                    Section = row.Entry.Definition.Section,
                    Key = row.Entry.Definition.Key,
                    Value = row.Entry.GetSerializedValue(),
                };
            }
        }

        /// <summary>
        /// Applies pasted settings text as one undo step. Values go through BepInEx's own parser,
        /// which logs and ignores a value it cannot read, so text pasted from someone else cannot
        /// set a setting to something its type does not allow. With <paramref name="onlyPage"/>,
        /// settings for other mods are skipped.
        /// </summary>
        public static Result Apply(string text, SettingsModel model, string undoLabel, Page onlyPage = null)
        {
            Result result = new Result();
            List<SettingsText.Record> records = SettingsText.Read(text, out result.Malformed);

            model.History.BeginGroup(undoLabel);
            model.RecordDepth++;
            try
            {
                foreach (SettingsText.Record record in records)
                {
                    if (!model.RowsById.TryGetValue(record.Id, out Row row)
                        || onlyPage != null && row.Page != onlyPage)
                    {
                        result.Unknown++;
                        continue;
                    }
                    if (row.IsReadOnly)
                    {
                        result.Unchanged++;
                        continue;
                    }
                    string before = row.Entry.GetSerializedValue();
                    if (before == record.Value)
                    {
                        result.Unchanged++;
                        continue;
                    }
                    row.Entry.SetSerializedValue(record.Value);
                    if (row.Entry.GetSerializedValue() == before)
                    {
                        result.Failed++;
                    }
                    else
                    {
                        result.Applied++;
                    }
                }
            }
            finally
            {
                model.RecordDepth--;
                model.History.EndGroup();
            }
            return result;
        }
    }
}
