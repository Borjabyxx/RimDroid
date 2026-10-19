using AutonomousColonists.Core;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AutonomousColonists;

/// <summary>Calls existing game finders; never copies their implementation or changes player policies.</summary>
internal sealed class NativeActionRuntime(AutonomousCoordinatorComponent owner) : IActionRuntime
{
    private readonly JobGiver_GetFood food = new();
    private readonly JobGiver_GetRest rest = new();
    private readonly JobGiver_PatientGoToBed patientBed = new() { respectTimetable = true };
    private readonly JobGiver_OptimizeApparel apparel = new();
    private readonly JobGiver_FleeDanger flee = new();
    private readonly JobGiver_FleeFire fleeFire = new();
    private List<WorkGiver_Scanner>? scanners;
    private AutonomyGameComponent Work => Current.Game.GetComponent<AutonomyGameComponent>();
    private List<WorkGiver_Scanner> Scanners => scanners ??= DefDatabase<WorkGiverDef>.AllDefsListForReading
        .Where(d => typeof(WorkGiver_Tend).IsAssignableFrom(d.giverClass) || typeof(WorkGiver_RescueDowned).IsAssignableFrom(d.giverClass))
        .OrderByDescending(d => d.priorityInType).ThenBy(d => d.defName, StringComparer.Ordinal)
        .Select(d => d.Worker).OfType<WorkGiver_Scanner>().ToList();

    internal static bool Manual(Pawn pawn) => pawn.Drafted || pawn.CurJob?.playerForced == true ||
        pawn.CurJob?.playerInterruptedForced == true || pawn.jobs?.jobQueue?.AnyPlayerForced == true;
    internal static bool WorkAllowed(Pawn pawn, WorkTypeDef? type) => type != null && pawn.workSettings?.Initialized == true &&
        pawn.workSettings.EverWork && !pawn.WorkTypeIsDisabled(type) && pawn.workSettings.GetPriority(type) > 0;
    private Pawn? FindPawn(DecisionCandidate c) => owner.FindObservedPawn(c.PawnId, c.MapId);
    public ExecutionFacts Inspect(DecisionCandidate c)
    {
        Pawn? pawn = FindPawn(c);
        if (pawn == null) return new(c.MapId, Unavailable: true);
        var job = pawn.CurJob;
        bool idle = job == null || job.def == JobDefOf.Wait_Wander || job.def == JobDefOf.GotoWander;
        bool suspendable = job?.workGiverDef != null && job.def.suspendable && job.def.playerInterruptible &&
            !job.def.forceCompleteBeforeNextJob && !job.workGiverDef.emergency;
        bool protectedJob = !idle && (!suspendable || Protected(job!));
        var assignment = pawn.timetable?.CurrentAssignment ?? TimeAssignmentDefOf.Anything;
        bool schedule = c.Action switch
        {
            ProposedAction.Rest => assignment != TimeAssignmentDefOf.Work && rest.GetPriority(pawn) > 0,
            ProposedAction.MedicalRest => assignment != TimeAssignmentDefOf.Work,
            ProposedAction.OptimizeApparel => assignment == TimeAssignmentDefOf.Anything || assignment == TimeAssignmentDefOf.Work,
            _ => true
        };
        bool module = owner.ModuleAllowed(c.Action);
        if (c.Action == ProposedAction.SelfTend) module &= pawn.playerSettings?.selfTend == true && WorkAllowed(pawn, WorkTypeDefOf.Doctor);
        if (c.Action == ProposedAction.TendPatient) module &= WorkAllowed(pawn, WorkTypeDefOf.Doctor);
        if (c.Action == ProposedAction.MedicalRest) module &= WorkAllowed(pawn, DefDatabase<WorkTypeDef>.GetNamedSilentFail("BedRest"));
        bool threat = (pawn.Map.dangerWatcher?.DangerRating ?? StoryDanger.None) != StoryDanger.None || pawn.Map.fireWatcher?.LargeFireDangerPresent == true;
        return new(pawn.Map.uniqueID, Manual(pawn), Work.IsExcluded(pawn), pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Downed || pawn.InMentalState,
            pawn.GetLord() != null || pawn.mindState?.duty != null, pawn.carryTracker?.CarriedThing != null,
            pawn.jobs?.jobQueue?.Count > 0, protectedJob, idle, suspendable, threat, schedule, module);
    }
    private static bool Protected(Job job) => job.def == JobDefOf.TendPatient || job.def == JobDefOf.Rescue ||
        job.def == JobDefOf.FeedPatient || job.def == JobDefOf.BeatFire || job.def == JobDefOf.ExtinguishSelf ||
        job.def == JobDefOf.Ingest || job.def == JobDefOf.LayDown || job.def == JobDefOf.Wear ||
        job.def == JobDefOf.Flee || job.def == JobDefOf.FleeAndCower || job.def == JobDefOf.Hunt ||
        job.def == JobDefOf.ManTurret || job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic;

    public IPreparedAction? Prepare(DecisionCandidate c)
    {
        var pawn = FindPawn(c);
        if (pawn == null || ExecutionRules.Denial(c, Inspect(c)) != null) return null;
        Job? job = null;
        ThinkNode_JobGiver? source = null;
        switch (c.Action)
        {
            case ProposedAction.Eat:
                if (FoodUtility.ShouldBeFedBySomeone(pawn)) return null;
                source = food; break;
            case ProposedAction.Rest: source = rest; break;
            case ProposedAction.MedicalRest: source = patientBed; break;
            case ProposedAction.OptimizeApparel: source = apparel; break;
            case ProposedAction.SeekSafety:
                if (pawn.GetAttachment(ThingDefOf.Fire) is Fire attached)
                    job = JobMaker.MakeJob(JobDefOf.ExtinguishSelf, attached);
                else if (pawn.Map.fireWatcher?.LargeFireDangerPresent == true)
                { source = fleeFire; job = source.TryIssueJobPackage(pawn, default).Job; }
                else if (pawn.playerSettings?.hostilityResponse == HostilityResponseMode.Flee)
                { source = flee; job = source.TryIssueJobPackage(pawn, default).Job; }
                break;
            case ProposedAction.SelfTend:
            case ProposedAction.TendPatient:
            case ProposedAction.Rescue:
                job = FindCareJob(pawn, c.Action); break;
        }
        if (job == null && source != null && c.Action != ProposedAction.SeekSafety) job = source.TryIssueJobPackage(pawn, default).Job;
        if (job == null) return null;
        var prepared = new NativePreparedAction(this, pawn, c, job);
        if (!prepared.Revalidate()) { prepared.Dispose(); return null; }
        return prepared;
    }

    private Job? FindCareJob(Pawn pawn, ProposedAction action)
    {
        bool self = action == ProposedAction.SelfTend, rescue = action == ProposedAction.Rescue;
        IEnumerable<Pawn> patients = self ? new[] { pawn } : pawn.Map.mapPawns.FreeColonistsSpawned
            .Where(p => p != pawn && !p.Dead && !p.Destroyed && !Work.IsExcluded(p) && !Manual(p))
            .OrderByDescending(HealthAIUtility.ShouldBeTendedNowByPlayerUrgent)
            .ThenBy(p => p.health?.summaryHealth?.SummaryHealthPercent ?? 1).ThenBy(p => p.ThingID, StringComparer.Ordinal);
        foreach (Pawn patient in patients)
        {
            if (!rescue && !HealthAIUtility.ShouldBeTendedNowByPlayer(patient)) continue;
            if (rescue && (!patient.Downed || patient.InBed())) continue;
            if (patient.IsForbidden(pawn) || !pawn.CanReserveAndReach(patient, PathEndMode.Touch, Danger.None)) continue;
            foreach (var scanner in Scanners)
            {
                if (rescue ? scanner is not WorkGiver_RescueDowned : scanner is not WorkGiver_Tend) continue;
                if (!rescue && self != (scanner is WorkGiver_TendSelf)) continue;
                if (!WorkAllowed(pawn, scanner.def.workType) || scanner.MissingRequiredCapacity(pawn) != null || scanner.ShouldSkip(pawn)) continue;
                if (!scanner.HasJobOnThing(pawn, patient, forced: false)) continue;
                Job? job = scanner.JobOnThing(pawn, patient, forced: false);
                if (job != null) { job.workGiverDef = scanner.def; return job; }
            }
        }
        return null;
    }

    private bool ValidateTargets(Pawn pawn, DecisionCandidate c, Job job)
    {
        if (job.playerForced || job.ignoreForbidden || job.ignoreDesignations || job.exitMapOnArrival) return false;
        bool allowed = c.Action switch
        {
            ProposedAction.Eat => job.def == JobDefOf.Ingest,
            ProposedAction.Rest or ProposedAction.MedicalRest => job.def == JobDefOf.LayDown && job.targetA.Thing is Building_Bed,
            ProposedAction.OptimizeApparel => job.def == JobDefOf.Wear && job.targetA.Thing is Apparel,
            ProposedAction.SelfTend or ProposedAction.TendPatient => job.def == JobDefOf.TendPatient && job.targetA.Thing is Pawn,
            ProposedAction.Rescue => job.def == JobDefOf.Rescue && job.targetA.Thing is Pawn && job.targetB.Thing is Building_Bed,
            ProposedAction.SeekSafety => job.def == JobDefOf.Flee || job.def == JobDefOf.ExtinguishSelf,
            _ => false
        };
        if (!allowed) return false;
        if (c.Action == ProposedAction.Eat)
        {
            Thing? target = job.targetA.Thing;
            if (target == null) return false;
            ThingDef edible = FoodUtility.GetFinalIngestibleDef(target);
            var policy = pawn.foodRestriction?.GetCurrentRespectedRestriction(pawn);
            if (edible == null || policy == null || !(edible == target.def ? policy.filter.Allows(target) : policy.filter.Allows(edible)) || !FoodUtility.WillEat(pawn, edible)) return false;
        }
        if (c.Action == ProposedAction.OptimizeApparel && job.targetA.Thing is Apparel garment)
        {
            if (pawn.outfits?.CurrentApparelPolicy?.filter.Allows(garment) != true || pawn.apparel.WouldReplaceLockedApparel(garment)) return false;
        }
        if (c.Action is ProposedAction.TendPatient or ProposedAction.SelfTend && job.targetA.Thing is Pawn patient)
        {
            if (Work.IsExcluded(patient) || Manual(patient) || !HealthAIUtility.ShouldBeTendedNowByPlayer(patient)) return false;
            if (patient == pawn && pawn.playerSettings?.selfTend != true) return false;
            if (job.targetB.Thing is Thing medicine && !patient.playerSettings.medCare.AllowsMedicine(medicine.def)) return false;
        }
        if (c.Action == ProposedAction.Rescue && job.targetA.Thing is Pawn rescued &&
            (Work.IsExcluded(rescued) || Manual(rescued) || !rescued.Downed || rescued.InBed())) return false;
        if (c.Action == ProposedAction.SeekSafety)
        {
            if (job.def == JobDefOf.ExtinguishSelf)
                return FireUtility.IsBurning(pawn) && job.targetA.Thing == pawn.GetAttachment(ThingDefOf.Fire);
            // target B is the danger being fled from, not a destination to approach.
            return SafeTarget(pawn, job.targetA);
        }
        return SafeTarget(pawn, job.targetA, c.Action == ProposedAction.Eat) &&
            SafeTarget(pawn, job.targetB, c.Action is ProposedAction.SelfTend or ProposedAction.TendPatient) && SafeTarget(pawn, job.targetC);
    }
    private static bool SafeTarget(Pawn pawn, LocalTargetInfo target, bool allowOwnInventory = false)
    {
        if (!target.IsValid) return true;
        if (target.HasThing)
        {
            Thing thing = target.Thing;
            if (thing.Destroyed) return false;
            if (!thing.Spawned)
                return allowOwnInventory && pawn.inventory?.innerContainer.Contains(thing) == true;
            if (thing.Map != pawn.Map || thing.IsForbidden(pawn) || FireUtility.IsBurning(thing)) return false;
        }
        IntVec3 cell = target.Cell;
        var area = pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
        return cell.InBounds(pawn.Map) && (area == null || area[cell]) && !cell.IsForbidden(pawn) &&
            !FireUtility.ContainsStaticFire(cell, pawn.Map) && GenTemperature.SafeTemperatureAtCell(pawn, cell, pawn.Map) &&
            cell.GetDangerFor(pawn, pawn.Map) == Danger.None && pawn.CanReach(target, PathEndMode.Touch, Danger.None);
    }

    private sealed class NativePreparedAction(NativeActionRuntime runtime, Pawn pawn, DecisionCandidate candidate,
        Job job) : IPreparedAction
    {
        private readonly Job? previous = pawn.CurJob;
        private bool handedOff;
        public string TargetKey => job.targetA.HasThing ? job.targetA.Thing.ThingID : "";
        public bool Revalidate() => !handedOff && pawn.CurJob == previous && ExecutionRules.Denial(candidate, runtime.Inspect(candidate)) == null &&
            runtime.ValidateTargets(pawn, candidate, job);
        public bool TryStart()
        {
            if (!Revalidate() || !job.TryMakePreToilReservations(pawn, errorOnFailed: false) || !Revalidate()) return false;
            // Our finder instances are not nodes in a saved ThinkTree. Job.ExposeData resolves
            // nonnegative giver keys through that tree; never persist a temporary finder there.
            job.jobGiver = null;
            job.jobGiverThinkTree = null;
            handedOff = true;
            pawn.jobs.StartJob(job, JobCondition.InterruptOptional, jobGiver: null,
                resumeCurJobAfterwards: previous?.def.suspendable == true, cancelBusyStances: false, preToilReservationsCanFail: true);
            return pawn.CurJob == job || pawn.jobs.jobQueue.Contains(job);
        }
        public void Dispose()
        {
            if (handedOff) return;
            pawn.ClearReservationsForJob(job);
            JobMaker.ReturnToPool(job);
        }
    }
}
