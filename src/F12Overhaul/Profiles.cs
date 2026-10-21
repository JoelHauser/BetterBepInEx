using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using F12Overhaul.Core;

namespace F12Overhaul
{
    /// <summary>
    /// Saved setting profiles, one text file each under BepInEx\config\F12Overhaul\profiles, in
    /// <see cref="ProfileFormat"/>. A profile holds every setting of every mod, not only the
    /// changed ones, so loading it puts back exactly what was saved; a setting changed since
    /// then is not left behind. Mods that are not installed are skipped on load and kept in the
    /// file untouched only until it is next overwritten.
    /// </summary>
    internal sealed class Profiles
    {
        public sealed class Result
        {
            public int Applied;
            public int Unchanged;
            public int Unknown;
            public int Failed;
            public int Malformed;
        }

        public string Folder { get; }

        public Profiles()
        {
            Folder = Path.Combine(Path.Combine(Paths.ConfigPath, "F12Overhaul"), "profiles");
        }

        public List<string> List()
        {
            try
            {
                if (!Directory.Exists(Folder))
                {
                    return new List<string>();
                }
                return Directory.GetFiles(Folder, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                F12OverhaulPlugin.Log.LogWarning("Could not list profiles in " + Folder + ": " + e.Message);
                return new List<string>();
            }
        }

        public bool Exists(string name)
        {
            string file = PathOf(name);
            return file != null && File.Exists(file);
        }

        public string PathOf(string name)
        {
            string safe = ProfileFormat.SafeFileName(name);
            return safe == null ? null : Path.Combine(Folder, safe + ".txt");
        }

        public DateTime? Modified(string name)
        {
            string file = PathOf(name);
            return file != null && File.Exists(file) ? File.GetLastWriteTime(file) : (DateTime?)null;
        }

        public void Save(string name, SettingsModel model)
        {
            string file = PathOf(name) ?? throw new ArgumentException("That name has no usable characters.");
            Directory.CreateDirectory(Folder);
            string text = ProfileFormat.Write(Records(model.AllRows), "Profile '" + name + "', saved " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            string temp = file + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
            File.Move(temp, file);
        }

        public string Read(string name)
        {
            string file = PathOf(name);
            return file == null ? null : File.ReadAllText(file);
        }

        public void Delete(string name)
        {
            string file = PathOf(name);
            if (file != null && File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public static IEnumerable<ProfileFormat.Record> Records(IEnumerable<Row> rows)
        {
            foreach (Row row in rows)
            {
                if (row.Entry == null || row.Id == null)
                {
                    continue;
                }
                yield return new ProfileFormat.Record
                {
                    Guid = row.Page.Guid,
                    Section = row.Entry.Definition.Section,
                    Key = row.Entry.Definition.Key,
                    Value = row.Entry.GetSerializedValue(),
                };
            }
        }

        /// <summary>
        /// Applies settings text (a profile or a paste) as one undo step. Values go through
        /// BepInEx's own parser, which logs and ignores a value it cannot read, so pasted text from
        /// someone else cannot set a setting to something its type does not allow.
        /// </summary>
        public static Result Apply(string text, SettingsModel model, string undoLabel, Page onlyPage = null)
        {
            Result result = new Result();
            List<ProfileFormat.Record> records = ProfileFormat.Read(text, out result.Malformed);

            model.History.BeginGroup(undoLabel);
            model.RecordDepth++;
            try
            {
                foreach (ProfileFormat.Record record in records)
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
