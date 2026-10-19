using System.Diagnostics;
using AutonomousColonists.Core;
using RimWorld;
using Verse;

namespace AutonomousColonists;

/// <summary>New game-wide coordinator. AutonomousColonists.AutonomyDirector remains the v0.2 adapter.</summary>
public sealed class AutonomousCoordinatorComponent : GameComponent
{
    private readonly AutonomousDecisionCoordinator core = new();
    private readonly List<PawnObservation> observations = new(64);
    private bool directorEnabled;
    private bool observationOnly = true;
    private AutonomyProfile profile = AutonomyProfile.Balanced;
    private int intervalOverride;
    private bool suspended;
    private List<DirectorMemoryEntry> savedMemory = new();
    public bool Enabled => directorEnabled;
    public bool ObservationOnly => observationOnly;
    public bool BlocksWorkWrites => directorEnabled && (observationOnly || core.Faulted);
    public string Status => core.Status;
    public string LastReason => core.LastReason;
    public int LastEvaluation => core.LastEvaluation;
    public long ProposedCount => core.ProposedCount;
    public long ExecutedCount => core.ExecutedCount;
    public double LastMilliseconds { get; private set; }
    public long LastAllocatedBytes { get; private set; }
    internal AutonomyProfile Profile => profile;
    public int IntervalOverride => intervalOverride;
    public AutonomousCoordinatorComponent(Game game) { }

    public void SetEnabled(bool value)
    {
        if (directorEnabled == value) return;
        directorEnabled = value;
        suspended = false;
        core.ResetFault();
        Configure();
    }
    public void SetObservation(bool value) { observationOnly = value; Configure(); }
    internal void SetProfile(AutonomyProfile value) { profile = Enum.IsDefined(value) ? value : AutonomyProfile.Balanced; Configure(); }
    public void SetInterval(int value) { intervalOverride = value == 0 ? 0 : Math.Clamp(value, 250, 2500); Configure(); }
    private void Configure()
    {
        core.Enabled = directorEnabled;
        core.ObservationOnly = observationOnly;
        core.Profile = profile;
        core.IntervalOverride = intervalOverride;
    }
    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            suspended = core.Faulted;
            savedMemory = core.ExportMemory().Select(m => new DirectorMemoryEntry(m)).ToList();
        }
        Scribe_Values.Look(ref directorEnabled, "directorEnabled", false);
        Scribe_Values.Look(ref observationOnly, "directorObservationOnly", true);
        Scribe_Values.Look(ref profile, "directorProfile", AutonomyProfile.Balanced);
        Scribe_Values.Look(ref intervalOverride, "directorIntervalOverride", 0);
        Scribe_Values.Look(ref suspended, "directorSuspended", false);
        Scribe_Collections.Look(ref savedMemory, "directorMemory", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (!Enum.IsDefined(profile)) profile = AutonomyProfile.Balanced;
            intervalOverride = intervalOverride == 0 ? 0 : Math.Clamp(intervalOverride, 250, 2500);
            Configure();
            savedMemory ??= new();
            core.ImportMemory(savedMemory.Where(m => m != null).Select(m => m.ToMemory()));
            savedMemory.Clear();
            if (suspended) core.Suspend("Suspensión conservada al cargar; desactiva y activa para recuperar");
        }
    }
    public override void GameComponentTick()
    {
        int tick = Find.TickManager.TicksGame;
        if (core.Due(tick)) EvaluateNow();
    }
    public void EvaluateNow()
    {
        if (!directorEnabled || core.Faulted) return;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        try
        {
            observations.Clear();
            var work = Current.Game.GetComponent<AutonomyGameComponent>();
            foreach (Map map in Find.Maps)
            {
                // Existing cached game watchers, read once per map. No thing/path/food/bed scans.
                var danger = map.dangerWatcher?.DangerRating ?? StoryDanger.None;
                bool threat = danger != StoryDanger.None;
                bool fire = map.fireWatcher?.LargeFireDangerPresent == true;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.Dead || pawn.Destroyed) continue;
                    bool excluded = work?.IsExcluded(pawn) == true;
                    // Excluded actors supply identity only so the core invalidates cached state.
                    if (excluded) { observations.Add(new(pawn.ThingID, map.uniqueID, null, null, null, Excluded: true)); continue; }
                    var job = pawn.CurJob;
                    bool manual = job?.playerForced == true || job?.playerInterruptedForced == true;
                    bool nonCombatant = pawn.WorkTagIsDisabled(WorkTags.Violent);
                    int cost = job == null ? 0 : job.def.forceCompleteBeforeNextJob || !job.def.playerInterruptible ? 100 : 40;
                    observations.Add(new(pawn.ThingID, map.uniqueID, pawn.needs?.food?.CurLevelPercentage,
                        pawn.needs?.rest?.CurLevelPercentage, pawn.health?.summaryHealth?.SummaryHealthPercent,
                        pawn.Drafted, manual, pawn.Downed || pawn.InMentalState,
                        CurrentJob: job?.def.defName ?? "", InterruptionCost: cost, Threat: threat, Fire: fire,
                        ImmediateDanger: FireUtility.IsBurning(pawn), NonCombatant: nonCombatant,
                        DefenseAuthorized: !nonCombatant && pawn.playerSettings?.hostilityResponse == HostilityResponseMode.Attack));
                }
            }
            core.Evaluate(observations, Find.TickManager.TicksGame, force: true);
            if (core.Faulted) Log.ErrorOnce("[Autonomous Colonists Director] " + core.LastReason, 17704101);
        }
        catch (Exception error)
        {
            core.Suspend("Error al observar: " + error.GetType().Name + ": " + error.Message);
            Log.ErrorOnce("[Autonomous Colonists Director] " + error, 17704102);
        }
        finally
        {
            LastMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LastAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
    }
}

internal sealed class DirectorMemoryEntry : IExposable
{
    private string id = "";
    private int mapId;
    private Urgency food;
    private Urgency rest;
    private ProposedAction action;
    private int priority;
    private Urgency urgency;
    private int tick = -1;
    public DirectorMemoryEntry() { }
    public DirectorMemoryEntry(ActorMemory value)
    {
        id = value.Id; mapId = value.MapId; food = value.Food; rest = value.Rest;
        action = value.LastAction; priority = value.LastPriority; urgency = value.LastUrgency; tick = value.LastDecisionTick;
    }
    public ActorMemory ToMemory() => new(id, mapId, food, rest, action, priority, urgency, tick);
    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id", "");
        Scribe_Values.Look(ref mapId, "mapId", 0);
        Scribe_Values.Look(ref food, "food", Urgency.Normal);
        Scribe_Values.Look(ref rest, "rest", Urgency.Normal);
        Scribe_Values.Look(ref action, "action", ProposedAction.None);
        Scribe_Values.Look(ref priority, "priority", 0);
        Scribe_Values.Look(ref urgency, "urgency", Urgency.Normal);
        Scribe_Values.Look(ref tick, "tick", -1);
    }
}
