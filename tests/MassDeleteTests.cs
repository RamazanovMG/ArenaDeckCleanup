using System;
using System.Collections.Generic;
using System.Linq;

static class MassDeleteTests
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
    static void Reject(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, name); }
    static ManagedDeck Deck(string name) { return new ManagedDeck { Id = Guid.NewGuid(), Name = name, Updated = new DateTime(2026,11,10) }; }
    static void Main()
    {
        BackupRetentionTests.Run();
        var a = Deck("Same name"); var b = Deck("Same name"); var c = Deck("New deck");
        var rows = new List<ManagedDeck> { a, b, c };
        Check(MassDeletePolicy.Targets(rows, new HashSet<Guid> { a.Id, c.Id }).Single().Id == b.Id, "Identify by ID despite duplicate names");
        Check(MassDeletePolicy.Targets(rows, new HashSet<Guid>()).Count == 3, "Explicit keep none");
        Check(MassDeletePolicy.Targets(rows, new HashSet<Guid>(rows.Select(d => d.Id))).Count == 0, "Keep all");
        var prefs = new Dictionary<Guid,bool> { {a.Id,true}, {b.Id,false} };
        var keep = MassDeletePolicy.InitialKeep(rows, prefs);
        Check(keep.Contains(a.Id) && !keep.Contains(b.Id) && keep.Contains(c.Id), "Remember selection and protect new decks");
        Reject(() => MassDeletePolicy.Targets(rows, new HashSet<Guid> { Guid.NewGuid() }), "Missing keeper stops operation");
        Reject(() => MassDeletePolicy.Validate(new List<ManagedDeck> { a, a }), "Duplicate IDs rejected");
        Reject(() => MassDeletePolicy.Validate(new List<ManagedDeck> { null }), "Null rows rejected");
        Reject(() => MassDeletePolicy.Validate(new List<ManagedDeck> { new ManagedDeck { Id=Guid.Empty, Name="X" } }), "Empty IDs rejected");
        Check(MassDeletePolicy.Same(rows, rows.AsEnumerable().Reverse().ToList()), "Reordering allowed");
        Check(!MassDeletePolicy.Same(rows, new List<ManagedDeck> {a,b}), "Missing deck detected");
        var replacement = Deck(a.Name);
        Check(!MassDeletePolicy.Same(rows, new List<ManagedDeck> {replacement,b,c}), "Identity replacement detected");
        replacement.Id=a.Id; replacement.Updated=a.Updated.AddMinutes(1);
        Check(!MassDeletePolicy.Same(rows, new List<ManagedDeck> {replacement,b,c}), "Edits detected");
        replacement.Updated=a.Updated; replacement.Name="Renamed";
        Check(!MassDeletePolicy.Same(rows, new List<ManagedDeck> {replacement,b,c}), "Renaming detected");
        Check(MassDeletePolicy.Targets(new List<ManagedDeck>(), new HashSet<Guid>()).Count==0, "Empty inventory handled");
        Console.WriteLine(passed + " permanent-plugin policy checks passed");
    }
}

