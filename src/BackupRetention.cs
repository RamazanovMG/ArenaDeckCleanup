using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class BackupRetention
{
    public const int Limit = 50;
    sealed class Record { public string Path; public JObject Deck; public Guid Id; public DateTime Time; public int Order; }
    public static int Apply(string root)
    {
        var paths = new List<string>();
        string directory = Path.Combine(root, "backups");
        if (Directory.Exists(directory)) paths.AddRange(Directory.GetFiles(directory, "decks.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal));
        string legacy = Path.Combine(root, "legacy-backup.json");
        if (File.Exists(legacy)) paths.Insert(0, legacy);
        var records = new List<Record>();
        // Read and validate everything before removing any recovery data.
        foreach (string path in paths)
        {
            DateTime fallback;
            string folder = Path.GetFileName(Path.GetDirectoryName(path));
            if (folder.Length < 15 || !DateTime.TryParseExact(folder.Substring(0, 15), "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out fallback))
                fallback = File.GetLastWriteTimeUtc(path);
            foreach (var token in JArray.Parse(File.ReadAllText(path)))
            {
                var deck = token as JObject;
                Guid id;
                if (deck == null || !Guid.TryParse((string)deck["Id"], out id) || id == Guid.Empty || deck["Summary"] == null || deck["Contents"] == null)
                    throw new InvalidDataException("Invalid deck backup: " + path);
                var time = deck["BackedUpAtUtc"] == null ? fallback : deck["BackedUpAtUtc"].ToObject<DateTime>().ToUniversalTime();
                records.Add(new Record { Path = path, Deck = deck, Id = id, Time = time, Order = records.Count });
            }
        }
        var selected = records.OrderByDescending(r => r.Time).ThenByDescending(r => r.Order).GroupBy(r => r.Id).Select(g => g.First()).Take(Limit).ToList();
        var keep = new HashSet<Record>(selected);
        foreach (string path in paths)
        {
            var original = records.Where(r => r.Path == path).ToList();
            var remaining = original.Where(keep.Contains).ToList();
            if (remaining.Count == original.Count) continue;
            if (remaining.Count == 0) File.Delete(path);
            else
            {
                // Preserve original backup time when rewriting a legacy file.
                foreach (var record in remaining) record.Deck["BackedUpAtUtc"] = record.Time;
                string temp = path + ".retention.tmp";
                File.WriteAllText(temp, new JArray(remaining.Select(r => r.Deck)).ToString(Formatting.Indented));
                File.Replace(temp, path, null);
            }
        }
        // Import-text copies must not retain decks that have expired.
        string exports = Path.Combine(root, "exports");
        var ids = new HashSet<Guid>(selected.Select(r => r.Id));
        if (Directory.Exists(exports)) foreach (string path in Directory.GetFiles(exports, "*.txt"))
        {
            Guid id;
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(path), out id) && !ids.Contains(id)) File.Delete(path);
        }
        return selected.Count;
    }
}
