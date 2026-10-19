namespace AutonomousColonists.Core;

internal readonly record struct ExecutionFacts(int MapId, bool Manual = false, bool Excluded = false,
    bool Unavailable = false, bool Duty = false, bool Carrying = false, bool Queued = false,
    bool ProtectedJob = false, bool Idle = true, bool SuspendableWork = false,
    bool Threat = false, bool ScheduleAllowed = true, bool ModuleAllowed = true);

internal static class ExecutionRules
{
    public static ActionModules ModuleFor(ProposedAction action) => action switch
    {
        ProposedAction.Eat => ActionModules.Food,
        ProposedAction.Rest => ActionModules.Rest,
        ProposedAction.SelfTend or ProposedAction.TendPatient or ProposedAction.MedicalRest => ActionModules.Health,
        ProposedAction.OptimizeApparel => ActionModules.Apparel,
        ProposedAction.SeekSafety or ProposedAction.Rescue => ActionModules.Safety,
        _ => ActionModules.None
    };
    public static bool Supports(ProposedAction action) => action is ProposedAction.Eat or ProposedAction.Rest or
        ProposedAction.SeekSafety or ProposedAction.SelfTend or ProposedAction.TendPatient or
        ProposedAction.MedicalRest or ProposedAction.OptimizeApparel or ProposedAction.Rescue;
    public static string? Denial(in DecisionCandidate c, in ExecutionFacts f)
    {
        if (!Supports(c.Action) || !f.ModuleAllowed) return "Módulo desactivado o acción sin ejecutor";
        if (c.MapId != f.MapId) return "El colono cambió de mapa";
        if (f.Manual) return "Orden manual o reclutamiento";
        if (f.Excluded || f.Unavailable || f.Duty || f.Carrying || f.Queued || f.ProtectedJob) return "Colono ocupado o protegido";
        if (!f.ScheduleAllowed) return "Horario del jugador";
        if (f.Threat && c.Action != ProposedAction.SeekSafety) return "Peligro del mapa: no buscar recursos";
        if (!f.Idle && !(c.Urgency == Urgency.Critical && f.SuspendableWork)) return "Evitar interrupción de tarea";
        return null;
    }
}

// Native finders may mutate timers. Observation returns before calling this interface.
internal interface IActionRuntime
{
    ExecutionFacts Inspect(DecisionCandidate decision);
    IPreparedAction? Prepare(DecisionCandidate decision);
}
internal interface IPreparedAction : IDisposable
{
    string TargetKey { get; }
    bool Revalidate();
    bool TryStart();
}
internal sealed record ExecutionMemory(string Id, int MapId, int LastTick);

/// <summary>Bounded, fair, synchronous dispatcher. Preparations are not executions.</summary>
internal sealed class ExecutionCoordinator
{
    private readonly Dictionary<string, ExecutionMemory> last = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Pawn, int Map, ProposedAction Action), int> attempts = new();
    private readonly HashSet<string> claimed = new(StringComparer.Ordinal);
    private readonly PriorityQueue<int, (int Urgency, int Priority, int Rotation)> pending = new(64);
    private readonly Dictionary<string, int> present = new(StringComparer.Ordinal);
    private int cursor;
    public long ExecutedCount { get; private set; }
    public int LastProbes { get; private set; }
    public ExecutionMemory[] ExportMemory() => last.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    public void ImportMemory(IEnumerable<ExecutionMemory> saved)
    {
        last.Clear(); attempts.Clear();
        foreach (var item in saved)
            if (!string.IsNullOrEmpty(item.Id) && item.LastTick >= 0) last.TryAdd(item.Id, item);
    }
    public void Retain(IReadOnlyList<PawnObservation> pawns)
    {
        present.Clear();
        foreach (var pawn in pawns) if (!pawn.Excluded) present[pawn.Id] = pawn.MapId;
        foreach (string id in last.Keys.Where(id => !present.TryGetValue(id, out int map) || last[id].MapId != map).ToArray()) last.Remove(id);
        foreach (var key in attempts.Keys.Where(k => !present.TryGetValue(k.Pawn, out int map) || k.Map != map).ToArray()) attempts.Remove(key);
    }
    public void Run(IReadOnlyList<DecisionCandidate> decisions, int tick, bool enabled, bool observation, bool actions,
        AutonomyProfile profile, IActionRuntime runtime, Action<DecisionCandidate, Authorization, string> report)
    {
        LastProbes = 0;
        if (!enabled || observation || !actions || decisions.Count == 0) return;
        int maximum = profile == AutonomyProfile.Conservative ? 1 : profile == AutonomyProfile.Advanced ? 3 : 2;
        int cooldown = profile == AutonomyProfile.Conservative ? 2500 : profile == AutonomyProfile.Advanced ? 750 : 1250;
        int executed = 0, start = cursor % decisions.Count;
        claimed.Clear();
        pending.Clear();
        for (int i = 0; i < decisions.Count; i++)
            if (decisions[i].Authorization == Authorization.AwaitingValidation) Enqueue(i, decisions, start);
        while (pending.Count > 0 && LastProbes < 4 && executed < maximum)
        {
            int index = pending.Dequeue();
            var c = decisions[index];
            if (last.TryGetValue(c.PawnId, out var previous) && previous.MapId == c.MapId && tick >= previous.LastTick && tick - previous.LastTick < cooldown)
            { report(c, Authorization.Cooldown, "Periodo de intervención mínima"); continue; }
            cursor = index + 1;
            bool accepted = TryCandidate(c, tick, runtime, report);
            if (accepted) executed++;
            else
                for (int i = index + 1; i < decisions.Count && decisions[i].PawnId == c.PawnId; i++)
                    if (decisions[i].Authorization == Authorization.Conflict && ExecutionRules.Supports(decisions[i].Action))
                    { Enqueue(i, decisions, start); break; }
        }
    }
    private void Enqueue(int index, IReadOnlyList<DecisionCandidate> decisions, int start)
    {
        var c = decisions[index];
        // Global urgency first; equal candidates rotate fairly between actors.
        pending.Enqueue(index, (-(int)c.Urgency, -c.Priority, (index - start + decisions.Count) % decisions.Count));
    }
    private bool TryCandidate(DecisionCandidate c, int tick, IActionRuntime runtime, Action<DecisionCandidate, Authorization, string> report)
    {
        var key = (c.PawnId, c.MapId, c.Action);
        if (attempts.TryGetValue(key, out int attempt) && tick >= attempt && tick - attempt < (c.Action == ProposedAction.OptimizeApparel ? 5000 : 1000)) return false;
        string? deny = ExecutionRules.Denial(c, runtime.Inspect(c));
        if (deny != null) { report(c, Authorization.Rejected, deny); return false; }
        attempts[key] = tick;
        LastProbes++;
        using var prepared = runtime.Prepare(c);
        if (prepared == null) { report(c, Authorization.Rejected, "Sin trabajo nativo permitido y seguro"); return false; }
        if (!string.IsNullOrEmpty(prepared.TargetKey) && claimed.Contains(prepared.TargetKey))
        { report(c, Authorization.Conflict, "Objetivo ya asignado en esta evaluación"); return false; }
        deny = ExecutionRules.Denial(c, runtime.Inspect(c));
        if (deny != null || !prepared.Revalidate()) { report(c, Authorization.Rejected, deny ?? "El objetivo dejó de ser válido"); return false; }
        if (!prepared.TryStart()) { report(c, Authorization.Rejected, "Inicio nativo rechazado"); return false; }
        if (!string.IsNullOrEmpty(prepared.TargetKey)) claimed.Add(prepared.TargetKey);
        last[c.PawnId] = new(c.PawnId, c.MapId, tick);
        ExecutedCount++;
        report(c, Authorization.Executed, "Trabajo automático aceptado por el juego; políticas conservadas");
        return true;
    }
}
