using System.Diagnostics;
using AutonomousColonists.Core;
using Verse;

namespace AutonomousColonists;

/// <summary>One ledger per game: moving between maps cannot give two directors ownership of a pawn.</summary>
public sealed class AutonomyGameComponent : GameComponent
{
    private const int Hour = 2500;
    private const int ObservationInterval = 250;
    private int lastWorkTick = -Hour;
    private int retryAfterTick;
    private Dictionary<string, int> originals = new();
    private Dictionary<string, int> applied = new();
    private Dictionary<string, int> seen = new();
    private List<string> manualLocks = new();
    private List<string> exclusions = new();
    private List<Pawn> tracked = new();
    private PriorityLedger? ledger;
    private bool? lastConflict;
    private bool forceRefresh = true;
    private bool failed;
    private bool enabledForSave;
    private List<string> recoveringIds = new();
    private List<WorkTypeDef>? sortedWorks;
    private readonly HashSet<string> recovering = new(StringComparer.Ordinal);
    private Dictionary<string, int> lastPawnMaps = new(StringComparer.Ordinal);
    public double LastMilliseconds { get; private set; }
    public int LastAssignments { get; private set; }
    public int LastWrites { get; private set; }
    private readonly List<string> coverageWarnings = new();
    public string CoverageWarnings => coverageWarnings.Count == 0 ? "Sin carencias urgentes detectadas" : string.Join("; ", coverageWarnings);
    public string Status { get; private set; } = "En espera";
    public bool EnabledForSave => enabledForSave;
    private PriorityLedger Ledger => ledger ??= new(originals, applied, manualLocks, seen);
    private List<WorkTypeDef> Works => sortedWorks ??= DefDatabase<WorkTypeDef>.AllDefsListForReading
        .OrderBy(w => WorkAllocator.Order(w.defName)).ThenBy(w => w.defName, StringComparer.Ordinal).ToList();

    public AutonomyGameComponent(Game game) { }
    public void SetEnabled(bool enabled)
    {
        enabledForSave = enabled;
        forceRefresh = true;
        Update(false);
    }
    public override void LoadedGame() => forceRefresh = true;

    private void ImportOldMaps()
    {
        foreach (Map map in Find.Maps) map.GetComponent<AutonomyDirector>()?.ImportInto(this);
    }

    internal void ImportLegacy(Dictionary<string, int> oldOriginals, Dictionary<string, int> oldApplied,
        IEnumerable<string> oldLocks, IEnumerable<string> oldExclusions, IEnumerable<Pawn> pawns)
    {
        foreach (var cell in oldOriginals)
        {
            if (!oldApplied.TryGetValue(cell.Key, out int value)) continue;
            if (Ledger.Applied.ContainsKey(cell.Key)) { Ledger.Locks.Add(cell.Key); continue; }
            Ledger.Record(cell.Key, cell.Value, value);
        }
        Ledger.Locks.UnionWith(oldLocks);
        foreach (string id in oldExclusions) if (!exclusions.Contains(id)) exclusions.Add(id);
        foreach (Pawn pawn in pawns) if (!tracked.Contains(pawn)) tracked.Add(pawn);
        forceRefresh = true;
    }
    public bool IsExcluded(Pawn pawn) => exclusions.Contains(pawn.ThingID);

    public void SetExcluded(Pawn pawn, bool excluded)
    {
        if (excluded && !IsExcluded(pawn))
        {
            exclusions.Add(pawn.ThingID);
        }
        else if (!excluded) exclusions.Remove(pawn.ThingID);
        forceRefresh = true;
        Update(false);
    }

    public void ReleaseManualLocks()
    {
        // Explicit opt-in in the settings window. Does not re-enable priority-zero cells.
        foreach (string key in Ledger.Locks.ToArray()) Ledger.Release(key);
        Ledger.Locks.Clear();
        forceRefresh = true;
    }

    public static bool WorkManagerConflict() => ModsConfig.IsActive("LordKuper.WorkManager");

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            manualLocks = Ledger.Locks.OrderBy(k => k, StringComparer.Ordinal).ToList();
            recoveringIds = recovering.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
        Scribe_Values.Look(ref enabledForSave, "enabledForSave", false);
        Scribe_Values.Look(ref lastWorkTick, "lastWorkTick", -Hour);
        Scribe_Collections.Look(ref originals, "originalPriorities", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref applied, "lastAppliedPriorities", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref seen, "observedPriorities", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref manualLocks, "manualLocks", LookMode.Value);
        Scribe_Collections.Look(ref exclusions, "excludedPawnIds", LookMode.Value);
        Scribe_Collections.Look(ref tracked, "trackedPawns", LookMode.Reference);
        Scribe_Collections.Look(ref recoveringIds, "recoveringPawnIds", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            originals ??= new(); applied ??= new(); seen ??= new(); manualLocks ??= new(); exclusions ??= new(); tracked ??= new();
            tracked.RemoveAll(p => p == null);
            recovering.Clear();
            if (recoveringIds != null) recovering.UnionWith(recoveringIds);
            ledger = new(originals, applied, manualLocks, seen);
            forceRefresh = true;
        }
    }

    public override void GameComponentTick()
    {
        // Constant cost on other ticks; no colony LINQ/scans/allocations there.
        int tick = Find.TickManager.TicksGame;
        if (tick % ObservationInterval != 0 || tick < retryAfterTick) return;
        Update(false);
    }

    public void RecalculateNow() => Update(true);
    public void SettingsChanged() { forceRefresh = true; Update(false); }

    private void Update(bool immediate)
    {
        AutonomousSettings? options = AutonomousColonistsMod.Settings;
        if (options == null) return;
        int tick = Find.TickManager.TicksGame;
        try
        {
            ImportOldMaps();
            // A single game-wide permission boundary also covers restoration and UI buttons.
            // Retain ownership while observing: leaving observation can resume or restore safely.
            if (Current.Game.GetComponent<AutonomousCoordinatorComponent>()?.BlocksWorkWrites == true)
            {
                if (Find.PlaySettings.useWorkPriorities) ObserveAll();
                Status = "Suspendido: Autonomous Director en observación o con error";
                forceRefresh = true;
                return;
            }
            bool conflict = WorkManagerConflict();
            if (conflict && lastConflict != true)
                Log.Warning("[Autonomous Colonists] Work Manager activo: automatización suspendida.");
            lastConflict = conflict;
            // RimWorld 1.7 GetPriority masks every positive raw value as 3 when this
            // setting is off. Observing/restoring that view would create false locks
            // and lose the original priorities. Keep the ledger until numbers return.
            if (!Find.PlaySettings.useWorkPriorities)
            {
                Status = "Pausado: activa prioridades numéricas para gestionar o restaurar";
                forceRefresh = true;
                if (!enabledForSave) failed = false;
                return;
            }
            if (conflict || !enabledForSave || failed)
            {
                RestoreAll();
                Status = conflict ? "Pausado: Work Manager" : failed ? "Pausado por error; desactiva y activa para reintentar"
                    : "Desactivado";
                if (!enabledForSave) failed = false;
                forceRefresh = true;
                return;
            }

            var present = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            var pawnMaps = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Map map in Find.Maps)
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                    if (!pawn.Dead && pawn.workSettings != null && pawn.workSettings.Initialized)
                    {
                        present.TryAdd(pawn.ThingID, pawn);
                        pawnMaps[pawn.ThingID] = map.uniqueID;
                    }
            if (pawnMaps.Count != lastPawnMaps.Count || pawnMaps.Any(p => !lastPawnMaps.TryGetValue(p.Key, out int previousMap) || previousMap != p.Value))
                forceRefresh = true;
            lastPawnMaps = pawnMaps;
            ObserveAll(); // Always runs BEFORE any possible write, including the button.
            foreach (Pawn pawn in tracked.ToArray())
            {
                if (present.ContainsKey(pawn.ThingID) && !IsExcluded(pawn)) continue;
                RestorePawn(pawn);
                if (pawn.Destroyed || pawn.Dead)
                {
                    Ledger.ForgetPawn(pawn.ThingID);
                    exclusions.Remove(pawn.ThingID);
                }
                if (!Ledger.HasOwnership(pawn.ThingID)) tracked.Remove(pawn);
                recovering.Remove(pawn.ThingID);
                forceRefresh = true;
            }
            var trackedIds = new HashSet<string>(tracked.Select(p => p.ThingID), StringComparer.Ordinal);
            foreach (Pawn pawn in present.Values)
            {
                if (IsExcluded(pawn)) continue;
                if (trackedIds.Add(pawn.ThingID)) { tracked.Add(pawn); forceRefresh = true; }
                // Temporary stress/unavailability triggers coverage on both loss and recovery.
                bool unavailable = Availability.Paused(pawn.Downed || pawn.InMentalState || pawn.Drafted || HasManualOrder(pawn),
                    pawn.needs?.mood?.CurLevelPercentage ?? 1f, pawn.needs?.rest?.CurLevelPercentage ?? 1f,
                    recovering.Contains(pawn.ThingID));
                if (unavailable ? recovering.Add(pawn.ThingID) : recovering.Remove(pawn.ThingID)) forceRefresh = true;
            }
            int interval = Math.Clamp(options.WorkUpdateHours, 1, 24) * Hour;
            if (!immediate && !forceRefresh && lastWorkTick >= 0 && tick >= lastWorkTick && tick - lastWorkTick < interval) return;
            var watch = Stopwatch.StartNew();
            LastAssignments = 0; LastWrites = 0;
            coverageWarnings.Clear();
            foreach (Map map in Find.Maps) ApplyMap(map, options);
            watch.Stop();
            LastMilliseconds = watch.Elapsed.TotalMilliseconds;
            lastWorkTick = tick;
            forceRefresh = false;
            Status = "Activo";
        }
        catch (Exception ex)
        {
            // No retries that repeatedly overwrite priorities after an error.
            failed = true; forceRefresh = true; retryAfterTick = tick + Hour;
            Log.ErrorOnce("[Autonomous Colonists] Automatización suspendida: " + ex, 17703107);
            try { RestoreAll(); } catch (Exception restoreError)
            { Log.ErrorOnce("[Autonomous Colonists] Restauración pendiente: " + restoreError, 17703108); }
            Status = "Pausado por error";
        }
    }

    private void ObserveAll()
    {
        int locksBefore = Ledger.Locks.Count;
        foreach (Pawn pawn in tracked)
        {
            if (pawn.workSettings == null || !pawn.workSettings.Initialized || pawn.Destroyed) continue;
            foreach (WorkTypeDef work in Works)
            {
                string key = PriorityLedger.Key(pawn.ThingID, work.defName);
                Ledger.Observe(key, pawn.workSettings.GetPriority(work));
            }
        }
        if (Ledger.Locks.Count != locksBefore) forceRefresh = true;
    }

    private void ApplyMap(Map map, AutonomousSettings options)
    {
        List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned
            .Where(p => !p.Dead && p.workSettings != null && p.workSettings.Initialized && p.workSettings.EverWork && p.skills != null && !IsExcluded(p))
            .OrderBy(p => p.ThingID, StringComparer.Ordinal).ToList();
        var byId = pawns.ToDictionary(p => p.ThingID, StringComparer.Ordinal);
        var requests = new List<WorkRequest>(Works.Count);
        foreach (WorkTypeDef work in Works)
        {
            int requested = options.WorkTargets.GetValueOrDefault(work.defName, -1);
            bool importantPending = work.defName == "Doctor" && map.mapPawns.FreeColonistsSpawned.Any(RimWorld.HealthAIUtility.ShouldBeTendedNowByPlayer) ||
                work.defName == "Firefighter" && map.fireWatcher?.LargeFireDangerPresent == true;
            if (importantPending && work.defName == "Doctor")
            {
                int waiting = map.mapPawns.FreeColonistsSpawned.Count(p => RimWorld.HealthAIUtility.ShouldBeTendedNowByPlayer(p) &&
                    !map.mapPawns.FreeColonistsSpawned.Any(d => d.CurJob?.def == RimWorld.JobDefOf.TendPatient && d.CurJob.targetA.Thing == p));
                if (waiting > 0) coverageWarnings.Add($"Mapa {map.uniqueID}: {waiting} pacientes pendientes sin atención en curso");
            }
            if (importantPending && work.defName == "Firefighter" && !map.mapPawns.FreeColonistsSpawned.Any(p =>
                p.CurJob?.def == RimWorld.JobDefOf.BeatFire || p.CurJob?.def == RimWorld.JobDefOf.ExtinguishFiresNearby))
                coverageWarnings.Add($"Mapa {map.uniqueID}: incendio importante sin extinción en curso");
            if (requested == 0)
            {
                if (importantPending) coverageWarnings.Add($"Mapa {map.uniqueID}: {work.LabelCap} pendiente; gestión pausada por el jugador");
                foreach (Pawn pawn in pawns) RestoreCell(pawn, work);
                continue;
            }
            var workers = new Worker[pawns.Count];
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                string key = PriorityLedger.Key(pawn.ThingID, work.defName);
                int current = pawn.workSettings.GetPriority(work);
                bool disabled = pawn.WorkTypeIsDisabled(work);
                workers[i] = new(pawn.ThingID, disabled ? 0 : WorkScoring.Aptitude(pawn, work), current,
                    !recovering.Contains(pawn.ThingID), disabled, Ledger.Observe(key, current),
                    Ledger.Applied.TryGetValue(key, out int previous) && previous <= (WorkAllocator.Housework(work.defName) ? 3 : 2));
            }
            requests.Add(new(work.defName, pawns.Count, workers, requested, work.relevantSkills?.Count > 0));
            if (importantPending && !workers.Any(w => w.Available && !w.Disabled && w.Current > 0))
                coverageWarnings.Add($"Mapa {map.uniqueID}: {work.LabelCap} sin trabajador elegible (se respetan 0 y exclusiones)");
        }
        var byWork = Works.ToDictionary(w => w.defName, StringComparer.Ordinal);
        foreach (Assignment assignment in WorkAllocator.Allocate(requests, options.Hysteresis))
        {
            Pawn pawn = byId[assignment.Pawn];
            WorkTypeDef work = byWork[assignment.Work];
            string key = PriorityLedger.Key(assignment.Pawn, assignment.Work);
            int current = pawn.workSettings.GetPriority(work);
            if (!CanWrite(pawn) || pawn.WorkTypeIsDisabled(work) || !Ledger.CanManage(key, current)) continue;
            if (current != assignment.Priority)
            {
                pawn.workSettings.SetPriority(work, assignment.Priority);
                LastWrites++;
            }
            Ledger.Record(key, current, assignment.Priority);
            LastAssignments++;
        }
    }

    private void RestoreCell(Pawn pawn, WorkTypeDef work)
    {
        if (!Find.PlaySettings.useWorkPriorities) return;
        string key = PriorityLedger.Key(pawn.ThingID, work.defName);
        if (!Ledger.Applied.ContainsKey(key)) return;
        if (pawn.workSettings == null || pawn.Destroyed || !pawn.workSettings.Initialized) { Ledger.Release(key); return; }
        if (!CanWrite(pawn)) return; // Defer restoration; do not discard the pending ownership.
        int current = pawn.workSettings.GetPriority(work);
        int? restore = Ledger.RestoreValue(key, current);
        if (restore.HasValue && !pawn.WorkTypeIsDisabled(work) && current != restore.Value)
            pawn.workSettings.SetPriority(work, restore.Value);
        // Incapable pawns are not written to; their disabled cell is released.
        Ledger.Release(key);
    }

    private void RestorePawn(Pawn pawn)
    {
        if (!Find.PlaySettings.useWorkPriorities) return;
        foreach (WorkTypeDef work in Works) RestoreCell(pawn, work);
        Ledger.ForgetObservations(pawn.ThingID);
    }

    private void RestoreAll()
    {
        if (!Find.PlaySettings.useWorkPriorities) return;
        foreach (Pawn pawn in tracked) RestorePawn(pawn);
        tracked.RemoveAll(p => !Ledger.HasOwnership(p.ThingID));
        recovering.Clear();
    }

    private static bool HasManualOrder(Pawn pawn) => NativeActionRuntime.Manual(pawn);
    private static bool CanWrite(Pawn pawn)
    {
        var coordinator = Current.Game.GetComponent<AutonomousCoordinatorComponent>();
        return WorkAuthorization.CanWrite(coordinator?.Enabled == true, coordinator?.ObservationOnly == true,
            coordinator?.BlocksWorkWrites == true, HasManualOrder(pawn), pawn.Drafted);
    }
}
