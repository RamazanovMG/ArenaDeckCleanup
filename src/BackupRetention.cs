using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
public static class BackupRetention
{
    // Compatibility entry point: count versions without deleting or rewriting data.
    public static int Apply(string root)
    {
        string directory = Path.Combine(root, "backups");
        var paths = Directory.Exists(directory) ? Directory.GetFiles(directory, "decks.json", SearchOption.AllDirectories).ToList() : new System.Collections.Generic.List<string>();
        string legacy = Path.Combine(root, "legacy-backup.json");
        if (File.Exists(legacy)) paths.Add(legacy);
        return paths.Sum(path => JArray.Parse(File.ReadAllText(path)).Count);
    }
}
