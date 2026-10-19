namespace AutonomousColonists.Core;

// Values are persisted: never reorder. Balanced deliberately has the default value.
internal enum AutonomyProfile { Balanced = 0, Conservative = 1, Advanced = 2 }
internal enum NeedKind { None, Food, Rest, Safety, Work, Health, Apparel }
internal enum Urgency { Normal, Growing, Important, Critical }
internal enum ModuleKind { Needs, Safety, Work, Health, Apparel }
internal enum ProposedAction { None, Eat, Rest, SeekSafety, EmergencyEnded, ReviewWorkAllocation, SelfTend, TendPatient, MedicalRest, OptimizeApparel, Rescue }
internal enum DecisionResource { Activity }
internal enum Authorization { Proposed, ManualOrder, Unavailable, Excluded, Conflict, Stability, ObservationOnly, Unsupported, MissingConditions, AwaitingValidation, Executed, Rejected, Cooldown, Budget }
[Flags]
internal enum DecisionConditions { None = 0, Available = 1, NoManualOrder = 2, AlternativeKnown = 4 }
[Flags]
internal enum ActionModules { None = 0, Food = 1, Rest = 2, Health = 4, Apparel = 8, Safety = 16, All = 31 }

internal readonly record struct PawnObservation(string Id, int MapId, float? Food, float? Rest,
    float? Health, bool Drafted = false, bool ManualOrder = false, bool Unavailable = false,
    bool Excluded = false, string CurrentJob = "", int InterruptionCost = 0,
    bool Threat = false, bool Fire = false, bool ImmediateDanger = false,
    bool NonCombatant = false, bool DefenseAuthorized = false,
    bool? FoodAvailable = null, bool? RestAvailable = null,
    bool NeedsTend = false, bool UrgentCare = false, bool CanDoctor = false,
    bool SelfTendAllowed = false, bool MedicalRestNeeded = false,
    bool PatientsNeedCare = false, bool UrgentPatient = false,
    bool ApparelReviewDue = false, bool RescueNeeded = false, bool CanRescue = false,
    int DoctorPriority = 4, int MedicineSkill = 0);

internal readonly record struct DecisionCandidate(string PawnId, int MapId, NeedKind Need,
    int Priority, Urgency Urgency, string Reason, ModuleKind Module, ProposedAction Action,
    DecisionConditions Required, DecisionConditions Satisfied,
    Authorization Authorization = Authorization.Proposed, DecisionResource Resource = DecisionResource.Activity);

internal readonly record struct DirectorPolicy(int Interval, int StabilityTicks, int ImprovementMargin,
    float FoodGrowing, float FoodImportant, float FoodCritical,
    float RestGrowing, float RestImportant, float RestCritical, Urgency Minimum)
{
    public static DirectorPolicy For(AutonomyProfile profile) => profile switch
    {
        AutonomyProfile.Conservative => new(1250, 2500, 100, .35f, .20f, .08f, .30f, .16f, .06f, Urgency.Important),
        AutonomyProfile.Advanced => new(250, 500, 50, .55f, .30f, .12f, .50f, .25f, .10f, Urgency.Growing),
        _ => new(750, 1000, 75, .45f, .25f, .10f, .40f, .20f, .08f, Urgency.Growing)
    };

    public static Urgency Classify(float? level, Urgency previous, float growing, float important, float critical)
    {
        if (!level.HasValue || !float.IsFinite(level.Value) || level < 0 || level > 1) return Urgency.Normal;
        float value = level.Value;
        // Escalate immediately, recover only after a five-percentage-point margin.
        if (value <= critical || previous == Urgency.Critical && value < critical + .05f) return Urgency.Critical;
        if (value <= important || previous >= Urgency.Important && value < important + .05f) return Urgency.Important;
        if (value <= growing || previous >= Urgency.Growing && value < growing + .05f) return Urgency.Growing;
        return Urgency.Normal;
    }
}

// Only these minimal values survive a save. No Pawn/Map/Job references or derived log entries.
internal sealed record ActorMemory(string Id, int MapId, Urgency Food = Urgency.Normal,
    Urgency Rest = Urgency.Normal, ProposedAction LastAction = ProposedAction.None,
    int LastPriority = 0, Urgency LastUrgency = Urgency.Normal, int LastDecisionTick = -1);

internal interface IAutonomyModule
{
    ModuleKind Kind { get; }
    void Propose(in PawnObservation pawn, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates);
}

internal static class WorkAuthorization
{
    public static bool CanWrite(bool directorEnabled, bool observation, bool faulted, bool manual, bool drafted) =>
        !manual && !drafted && !(directorEnabled && (observation || faulted));
}
