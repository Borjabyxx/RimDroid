namespace AutonomousColonists.Core;

internal sealed class NeedsAutonomy : IAutonomyModule
{
    public ModuleKind Kind => ModuleKind.Needs;
    public void Propose(in PawnObservation pawn, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        Add(pawn, memory.Food, NeedKind.Food, ProposedAction.Eat, pawn.FoodAvailable, policy, candidates);
        Add(pawn, memory.Rest, NeedKind.Rest, ProposedAction.Rest, pawn.RestAvailable, policy, candidates);
    }
    private static void Add(in PawnObservation pawn, Urgency urgency, NeedKind need, ProposedAction action,
        bool? alternative, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        if (urgency < policy.Minimum) return;
        int priority = urgency switch { Urgency.Critical => 900, Urgency.Important => 600, _ => 300 };
        // Critical needs are not diminished by the current task's cost; no job is interrupted here.
        if (urgency != Urgency.Critical) priority -= Math.Clamp(pawn.InterruptionCost, 0, 100);
        var conditions = (!pawn.Unavailable ? DecisionConditions.Available : 0) |
            (!pawn.ManualOrder && !pawn.Drafted ? DecisionConditions.NoManualOrder : 0) |
            (alternative == true ? DecisionConditions.AlternativeKnown : 0);
        string context = pawn.Health is < .5f ? "; salud reducida" : "";
        candidates.Add(new(pawn.Id, pawn.MapId, need, priority, urgency,
            (need == NeedKind.Food ? "Hambre " : "Descanso ") + urgency + context +
            (alternative.HasValue ? alternative.Value ? "; alternativa conocida" : "; sin alternativa" : "; alternativa sin verificar"),
            ModuleKind.Needs, action, DecisionConditions.Available | DecisionConditions.NoManualOrder |
            DecisionConditions.AlternativeKnown, conditions));
    }
}

internal sealed class SafetyAutonomy : IAutonomyModule
{
    public ModuleKind Kind => ModuleKind.Safety;
    public void Propose(in PawnObservation pawn, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        if (pawn.RescueNeeded && pawn.CanRescue)
            candidates.Add(new(pawn.Id, pawn.MapId, NeedKind.Safety, 850, Urgency.Important,
                "Colono caído pendiente de rescate; validar paciente, cama y ruta sin peligro", Kind, ProposedAction.Rescue,
                DecisionConditions.Available | DecisionConditions.NoManualOrder | DecisionConditions.AlternativeKnown,
                (!pawn.Unavailable ? DecisionConditions.Available : 0) | (!pawn.ManualOrder && !pawn.Drafted ? DecisionConditions.NoManualOrder : 0)));
        if (!pawn.Threat && !pawn.Fire && !pawn.ImmediateDanger) return;
        string reason = pawn.ImmediateDanger ? "Peligro inmediato: colono ardiendo" : pawn.Fire ? "Incendio importante en el mapa" : "Amenaza detectada en el mapa";
        reason += pawn.NonCombatant ? "; no combatiente" : pawn.DefenseAuthorized ? "; respuesta defensiva autorizada por el jugador" : "; defensa no autorizada";
        candidates.Add(new(pawn.Id, pawn.MapId, NeedKind.Safety, pawn.ImmediateDanger ? 1100 : 750,
            pawn.ImmediateDanger ? Urgency.Critical : Urgency.Important, reason + "; proponer refugio, ubicación sin verificar",
            Kind, ProposedAction.SeekSafety, DecisionConditions.Available | DecisionConditions.NoManualOrder | DecisionConditions.AlternativeKnown,
            (!pawn.Unavailable ? DecisionConditions.Available : 0) | (!pawn.ManualOrder && !pawn.Drafted ? DecisionConditions.NoManualOrder : 0)));
    }
}

// Adapter proposals do not replace the v0.3 allocator or create another priority writer.
internal sealed class WorkAutonomy : IAutonomyModule
{
    public ModuleKind Kind => ModuleKind.Work;
    public void Propose(in PawnObservation pawn, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        if (memory.Food < Urgency.Important && memory.Rest < Urgency.Important) return;
        candidates.Add(new(pawn.Id, pawn.MapId, NeedKind.Work, 200, Urgency.Important,
            "Necesidad personal importante; propuesta de revisar cobertura sin adelantar el cálculo v0.3", Kind,
            ProposedAction.ReviewWorkAllocation, DecisionConditions.Available | DecisionConditions.NoManualOrder,
            (!pawn.Unavailable ? DecisionConditions.Available : 0) | (!pawn.ManualOrder && !pawn.Drafted ? DecisionConditions.NoManualOrder : 0)));
    }
}

internal sealed class HealthAutonomy : IAutonomyModule
{
    public ModuleKind Kind => ModuleKind.Health;
    public void Propose(in PawnObservation p, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        if (p.NeedsTend && p.SelfTendAllowed && p.CanDoctor)
            Add(p, candidates, ProposedAction.SelfTend, p.UrgentCare, "Autocuidado permitido; validar medicina y reservas");
        if (p.CanDoctor && p.PatientsNeedCare)
            Add(p, candidates, ProposedAction.TendPatient, p.UrgentPatient, "Paciente pendiente; priorizar urgencia y coordinar reservas");
        if (p.MedicalRestNeeded)
            Add(p, candidates, ProposedAction.MedicalRest, p.UrgentCare, "Recuperación médica; respetar horario y cama disponible");
    }
    private static void Add(in PawnObservation p, List<DecisionCandidate> candidates, ProposedAction action, bool urgent, string reason) =>
        candidates.Add(new(p.Id, p.MapId, NeedKind.Health, urgent ? 980 : 650, urgent ? Urgency.Critical : Urgency.Important,
            reason, ModuleKind.Health, action, DecisionConditions.Available | DecisionConditions.NoManualOrder | DecisionConditions.AlternativeKnown,
            (!p.Unavailable ? DecisionConditions.Available : 0) | (!p.ManualOrder && !p.Drafted ? DecisionConditions.NoManualOrder : 0)));
}

internal sealed class ApparelAutonomy : IAutonomyModule
{
    public ModuleKind Kind => ModuleKind.Apparel;
    public void Propose(in PawnObservation p, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates)
    {
        if (!p.ApparelReviewDue) return;
        candidates.Add(new(p.Id, p.MapId, NeedKind.Apparel, 150, Urgency.Growing,
            "Revisar ropa, desgaste y abrigo con la política de vestimenta nativa", Kind, ProposedAction.OptimizeApparel,
            DecisionConditions.Available | DecisionConditions.NoManualOrder | DecisionConditions.AlternativeKnown,
            (!p.Unavailable ? DecisionConditions.Available : 0) | (!p.ManualOrder && !p.Drafted ? DecisionConditions.NoManualOrder : 0)));
    }
}
