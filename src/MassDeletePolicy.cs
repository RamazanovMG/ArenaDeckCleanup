using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ManagedDeck
{
    public Guid Id;
    public string Name;
    public string Format;
    public DateTime Updated;
}

public static class MassDeletePolicy
{
    public static void Validate(List<ManagedDeck> decks)
    {
        if (decks.Any(d => d == null || d.Id == Guid.Empty || d.Name == null))
            throw new Exception("A deck has an invalid identity.");
        if (decks.Select(d => d.Id).Distinct().Count() != decks.Count)
            throw new Exception("Duplicate deck IDs were returned.");
    }
    public static List<ManagedDeck> Targets(List<ManagedDeck> decks, HashSet<Guid> keep)
    {
        Validate(decks);
        if (keep.Any(id => !decks.Any(d => d.Id == id)))
            throw new Exception("A selected keeper is missing. Refresh the list.");
        return decks.Where(d => !keep.Contains(d.Id)).ToList();
    }
    public static bool Same(List<ManagedDeck> a, List<ManagedDeck> b)
    {
        Validate(a); Validate(b);
        return a.Count == b.Count && a.All(x => b.Any(y => x.Id == y.Id && x.Name == y.Name && x.Updated == y.Updated));
    }
    public static HashSet<Guid> InitialKeep(List<ManagedDeck> decks, Dictionary<Guid, bool> preferences)
    {
        Validate(decks);
        // A newly imported deck is protected until explicitly unchecked.
        return new HashSet<Guid>(decks.Where(d => !preferences.ContainsKey(d.Id) || preferences[d.Id]).Select(d => d.Id));
    }
}
