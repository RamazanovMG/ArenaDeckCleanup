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
        string beforeFirst = File.ReadAllText(Path.Combine(first,"decks.json"));
        string beforeSecond = File.ReadAllText(Path.Combine(second,"decks.json"));
        Check(BackupRetention.Apply(root)==65,"all 65 retained");
        Check(File.ReadAllText(Path.Combine(first,"decks.json"))==beforeFirst && File.ReadAllText(Path.Combine(second,"decks.json"))==beforeSecond,"backups unchanged");
        Check(File.Exists(Path.Combine(root,"exports",ids[0]+".txt")),"old exports preserved");
        Check(BackupRetention.Apply(root)==65,"repeat stable");
        var duplicate=deck(64); duplicate["BackedUpAtUtc"]=new DateTime(2027,1,1,0,0,0,DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(root,"legacy-backup.json"),new JArray(duplicate).ToString());
        Check(BackupRetention.Apply(root)==66,"all historical versions retained");
        Check(File.ReadAllText(Path.Combine(second,"decks.json"))==beforeSecond,"older version unchanged");
        string previous=File.ReadAllText(Path.Combine(first,"decks.json"));
        File.WriteAllText(Path.Combine(root,"legacy-backup.json"),"broken");
        bool rejected=false; try {BackupRetention.Apply(root);} catch {rejected=true;}
        Check(rejected && File.ReadAllText(Path.Combine(first,"decks.json"))==previous,"invalid data fails before pruning");
        Console.WriteLine("Unlimited backup checks passed: over 50 decks, historical versions, exports, repeat stability, invalid data.");
    }
}
