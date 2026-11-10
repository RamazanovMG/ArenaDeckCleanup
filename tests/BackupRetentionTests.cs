using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
static class BackupRetentionTests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ArenaRetention-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "backups", "20260101-000000-a");
        string second = Path.Combine(root, "backups", "20260102-000000-b");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second); Directory.CreateDirectory(Path.Combine(root,"exports"));
        var ids = Enumerable.Range(0, 65).Select(i => Guid.NewGuid()).ToArray();
        Func<int,JObject> deck = i => new JObject { ["Id"] = ids[i].ToString(), ["Summary"] = new JObject(), ["Contents"] = new JObject(), ["BackedUpAtUtc"] = new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc).AddMinutes(i) };
        File.WriteAllText(Path.Combine(first,"decks.json"), new JArray(Enumerable.Range(0,40).Select(deck)).ToString());
        File.WriteAllText(Path.Combine(second,"decks.json"), new JArray(Enumerable.Range(40,25).Select(deck)).ToString());
        File.WriteAllText(Path.Combine(root,"exports",ids[0]+".txt"), "old deck");
        File.WriteAllText(Path.Combine(root,"exports",ids[64]+".txt"), "kept deck");
        Check(BackupRetention.Apply(root)==50,"limit");
        var kept=Directory.GetFiles(root,"decks.json",SearchOption.AllDirectories).SelectMany(p=>JArray.Parse(File.ReadAllText(p))).Select(t=>(string)t["Id"]).ToArray();
        Check(kept.Length==50 && ids.Skip(15).All(id=>kept.Contains(id.ToString())),"exact newest 50");
        Check(!File.Exists(Path.Combine(root,"exports",ids[0]+".txt")),"expired export removed");
        Check(File.Exists(Path.Combine(root,"exports",ids[64]+".txt")),"kept export preserved");
        Check(BackupRetention.Apply(root)==50,"repeat stable");
        var duplicate=deck(64); duplicate["BackedUpAtUtc"]=new DateTime(2027,1,1,0,0,0,DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(root,"legacy-backup.json"),new JArray(duplicate).ToString());
        Check(BackupRetention.Apply(root)==50,"duplicate counts once");
        Check(JArray.Parse(File.ReadAllText(Path.Combine(second,"decks.json"))).Count==24,"old duplicate removed");
        string previous=File.ReadAllText(Path.Combine(first,"decks.json"));
        File.WriteAllText(Path.Combine(root,"legacy-backup.json"),"broken");
        bool rejected=false; try {BackupRetention.Apply(root);} catch {rejected=true;}
        Check(rejected && File.ReadAllText(Path.Combine(first,"decks.json"))==previous,"invalid data fails before pruning");
        Console.WriteLine("Backup retention checks passed: exact cutoff, duplicate versions, exports, repeat stability, invalid data.");
    }
}
