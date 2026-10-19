namespace AutonomousColonists.Core;

/// <summary>Deterministic, main-thread, proposal-only coordinator. No Verse references or job executor.</summary>
internal sealed class AutonomousDecisionCoordinator
{
    private readonly IAutonomyModule[] modules;
    private readonly Dictionary<string, ActorMemory> memory = new(StringComparer.Ordinal);
    private readonly HashSet<string> emergencies = new(StringComparer.Ordinal);
    private readonly List<DecisionCandidate> candidates = new(8);
    private readonly List<DecisionCandidate> decisions = new(64);
    private readonly HashSet<string> present = new(StringComparer.Ordinal);
    private readonly Queue<DecisionCandidate> journal = new(64);
    private AutonomyProfile profile;
    private int lastEvaluation = -1;
    public bool Enabled { get; set; }
    public bool ObservationOnly { get; set; } = true;
    public bool Faulted { get; private set; }
    public int IntervalOverride { get; set; }
    public int LastEvaluation => lastEvaluation;
    public long ProposedCount { get; private set; }
    public long ExecutedCount => 0; // v0.4 deliberately has no executor for these actions.
    public string LastReason { get; private set; } = "Sin decisiones";
    public string Status => !Enabled ? "Desactivado" : Faulted ? "Suspendido por error; desactiva y activa para recuperar"
        : ObservationOnly ? "Observación: sin escrituras" : "Propuestas; ejecución de necesidades y seguridad no disponible";
    public IReadOnlyCollection<DecisionCandidate> Journal => journal;
    public AutonomyProfile Profile
    {
        get => profile;
        set
        {
            var next = Enum.IsDefined(value) ? value : AutonomyProfile.Balanced;
            if (next == profile) return;
            profile = next; memory.Clear(); emergencies.Clear(); lastEvaluation = -1;
        }
    }
    public AutonomousDecisionCoordinator(params IAutonomyModule[] modules) =>
        this.modules = modules.Length == 0 ? [new SafetyAutonomy(), new NeedsAutonomy(), new WorkAutonomy()] :
            modules.OrderBy(m => m.Kind).ToArray();

    public bool Due(int tick) => Enabled && !Faulted && (lastEvaluation < 0 || tick < lastEvaluation ||
        tick - lastEvaluation >= (IntervalOverride == 0 ? DirectorPolicy.For(Profile).Interval : Math.Clamp(IntervalOverride, 250, 2500)));
    public void ResetFault() { Faulted = false; lastEvaluation = -1; memory.Clear(); emergencies.Clear(); }
    public void Suspend(string reason) { Faulted = true; LastReason = reason; }
    public ActorMemory[] ExportMemory() => memory.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    public void ImportMemory(IEnumerable<ActorMemory> saved)
    {
        memory.Clear(); emergencies.Clear(); lastEvaluation = -1;
        foreach (var item in saved)
        {
            if (string.IsNullOrEmpty(item.Id) || !Enum.IsDefined(item.Food) || !Enum.IsDefined(item.Rest) ||
                !Enum.IsDefined(item.LastAction) || !Enum.IsDefined(item.LastUrgency) || item.LastDecisionTick < -1) continue;
            memory.TryAdd(item.Id, item);
        }
    }

    public IReadOnlyList<DecisionCandidate> Evaluate(IReadOnlyList<PawnObservation> observations, int tick, bool force = false)
    {
        decisions.Clear();
        if (!Enabled || Faulted || !force && !Due(tick)) return decisions;
        try
        {
            var policy = DirectorPolicy.For(Profile);
            present.Clear();
            foreach (var pawn in observations.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (!present.Add(pawn.Id)) throw new InvalidOperationException("Observación duplicada: " + pawn.Id);
                if (pawn.Excluded) { memory.Remove(pawn.Id); emergencies.Remove(pawn.Id); continue; }
                if (!memory.TryGetValue(pawn.Id, out var state) || state.MapId != pawn.MapId || tick < state.LastDecisionTick)
                {
                    state = new(pawn.Id, pawn.MapId);
                    emergencies.Remove(pawn.Id);
                }
                state = state with
                {
                    Food = DirectorPolicy.Classify(pawn.Food, state.Food, policy.FoodGrowing, policy.FoodImportant, policy.FoodCritical),
                    Rest = DirectorPolicy.Classify(pawn.Rest, state.Rest, policy.RestGrowing, policy.RestImportant, policy.RestCritical)
                };
                candidates.Clear();
                foreach (var module in modules) module.Propose(pawn, state, policy, candidates);
                bool emergency = pawn.Threat || pawn.Fire || pawn.ImmediateDanger;
                if (!emergency && emergencies.Remove(pawn.Id)) candidates.Add(new(pawn.Id, pawn.MapId, NeedKind.Safety, 0,
                    Urgency.Normal, "Emergencia finalizada según los observadores", ModuleKind.Safety, ProposedAction.EmergencyEnded,
                    DecisionConditions.None, DecisionConditions.None));
                if (emergency) emergencies.Add(pawn.Id);
                candidates.Sort(Compare);
                // Preserve a still-valid incumbent even after the minimum stability period.
                // A tie must not switch activities merely because of the deterministic tie-breaker.
                if (candidates.Count > 1 && state.LastAction != ProposedAction.None)
                {
                    int incumbent = -1;
                    for (int i = 0; i < candidates.Count; i++)
                        if (candidates[i].Action == state.LastAction) { incumbent = i; break; }
                    if (incumbent > 0 && candidates[incumbent].Urgency >= candidates[0].Urgency &&
                        candidates[0].Priority - candidates[incumbent].Priority < policy.ImprovementMargin)
                    {
                        var retained = candidates[incumbent];
                        candidates.RemoveAt(incumbent); candidates.Insert(0, retained);
                    }
                }
                bool occupied = false;
                bool recordedReason = false;
                foreach (var proposal in candidates)
                {
                    if (proposal.PawnId != pawn.Id || proposal.MapId != pawn.MapId)
                        throw new InvalidOperationException("Módulo propuso otro colono o mapa");
                    Authorization authorization;
                    if (pawn.ManualOrder || pawn.Drafted) authorization = Authorization.ManualOrder;
                    else if (pawn.Unavailable) authorization = Authorization.Unavailable;
                    else if (occupied) authorization = Authorization.Conflict;
                    else
                    {
                        occupied = true;
                        bool within = state.LastDecisionTick >= 0 && tick >= state.LastDecisionTick && tick - state.LastDecisionTick < policy.StabilityTicks;
                        bool same = state.LastAction == proposal.Action && state.LastUrgency == proposal.Urgency;
                        bool improvement = proposal.Urgency > state.LastUrgency || proposal.Priority >= state.LastPriority + policy.ImprovementMargin;
                        if (within && (same || !improvement)) authorization = Authorization.Stability;
                        else
                        {
                            state = state with { LastAction = proposal.Action, LastPriority = proposal.Priority,
                                LastUrgency = proposal.Urgency, LastDecisionTick = tick };
                            authorization = ObservationOnly ? Authorization.ObservationOnly :
                                (proposal.Satisfied & proposal.Required) != proposal.Required ? Authorization.MissingConditions : Authorization.Unsupported;
                        }
                    }
                    var decision = proposal with { Authorization = authorization };
                    decisions.Add(decision);
                    ProposedCount++;
                    if (journal.Count == 64) journal.Dequeue();
                    journal.Enqueue(decision);
                    if (!recordedReason) { LastReason = decision.Reason + " [" + authorization + "]"; recordedReason = true; }
                }
                memory[pawn.Id] = state;
            }
            foreach (string id in memory.Keys.Where(id => !present.Contains(id)).ToArray()) { memory.Remove(id); emergencies.Remove(id); }
            lastEvaluation = tick;
        }
        catch (Exception error)
        {
            Suspend("Evaluación suspendida: " + error.GetType().Name + ": " + error.Message);
            decisions.Clear();
        }
        return decisions;
    }
    private static int Compare(DecisionCandidate left, DecisionCandidate right)
    {
        int result = right.Priority.CompareTo(left.Priority);
        if (result == 0) result = right.Urgency.CompareTo(left.Urgency);
        if (result == 0) result = left.Module.CompareTo(right.Module);
        if (result == 0) result = left.Action.CompareTo(right.Action);
        if (result == 0) result = string.CompareOrdinal(left.Reason, right.Reason);
        return result;
    }
}
