using Verse;

namespace AutonomousColonists;

/// <summary>Retains the v0.2 saved MapComponent type. New management lives in the game component.</summary>
public sealed class AutonomyDirector : MapComponent
{
    private Dictionary<string, int> originals = new();
    private Dictionary<string, int> applied = new();
    private List<string> locks = new();
    private List<string> excluded = new();

    public AutonomyDirector(Map map) : base(map) { }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref originals, "originalPriorities", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref applied, "lastAppliedPriorities", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref locks, "manualLocks", LookMode.Value);
        Scribe_Collections.Look(ref excluded, "excludedPawnIds", LookMode.Value);
        originals ??= new(); applied ??= new(); locks ??= new(); excluded ??= new();
    }

    internal void ImportInto(AutonomyGameComponent director)
    {
        if (originals.Count == 0 && applied.Count == 0 && locks.Count == 0 && excluded.Count == 0) return;
        director.ImportLegacy(originals, applied, locks, excluded, map.mapPawns.FreeColonistsSpawned);
        originals.Clear(); applied.Clear(); locks.Clear(); excluded.Clear();
    }
}
