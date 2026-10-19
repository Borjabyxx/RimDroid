namespace AutonomousColonists.Core;

/// <summary>Ownership of observed priority values, independent of the game runtime.</summary>
internal sealed class PriorityLedger
{
    public Dictionary<string, int> Original { get; }
    public Dictionary<string, int> Applied { get; }
    public HashSet<string> Locks { get; }
    public Dictionary<string, int> Seen { get; }

    public PriorityLedger(Dictionary<string, int>? original = null,
        Dictionary<string, int>? applied = null, IEnumerable<string>? locks = null, Dictionary<string, int>? seen = null)
    {
        Original = original ?? new(StringComparer.Ordinal);
        Applied = applied ?? new(StringComparer.Ordinal);
        Locks = locks == null ? new(StringComparer.Ordinal) : new(locks, StringComparer.Ordinal);
        Seen = seen ?? new(StringComparer.Ordinal);
    }

    public static string Key(string pawn, string work) => pawn + "|" + work;

    public bool Observe(string key, int current)
    {
        if (Applied.TryGetValue(key, out int last) ? current != last : Seen.TryGetValue(key, out int observed) && current != observed)
            Locks.Add(key);
        Seen[key] = current;
        return Locks.Contains(key);
    }

    public bool CanManage(string key, int current) => !Observe(key, current) && current is >= 1 and <= 4;

    // Commit only AFTER SetPriority succeeds; a failed write never claims ownership.
    public void Record(string key, int original, int applied)
    {
        Original.TryAdd(key, original);
        Applied[key] = applied;
        Seen[key] = applied;
    }

    public int? RestoreValue(string key, int current)
    {
        if (Observe(key, current) || !Original.TryGetValue(key, out int original) ||
            !Applied.ContainsKey(key)) return null;
        return original;
    }

    public void Release(string key)
    {
        Original.Remove(key);
        Applied.Remove(key);
        Seen.Remove(key);
        // A player's lock outlives exclusion, suspension, and map changes.
    }

    public void ForgetPawn(string pawn)
    {
        string prefix = pawn + "|";
        foreach (string key in Original.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) Release(key);
        Locks.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal));
        ForgetObservations(pawn);
    }

    public void ForgetObservations(string pawn)
    {
        string prefix = pawn + "|";
        foreach (string key in Seen.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) Seen.Remove(key);
    }
}
