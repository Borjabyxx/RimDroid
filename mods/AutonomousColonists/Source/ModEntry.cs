using UnityEngine;
using Verse;
using AutonomousColonists.Core;

namespace AutonomousColonists;

public sealed class AutonomousSettings : ModSettings
{
    // Opt in on a test save. The vanilla medical/needs policies are never written.
    public int WorkUpdateHours = 3;
    public float Hysteresis = 6f;
    public Dictionary<string, int> WorkTargets = new();

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref WorkUpdateHours, "workUpdateHours", 3);
        Scribe_Values.Look(ref Hysteresis, "hysteresis", 6f);
        Scribe_Collections.Look(ref WorkTargets, "workTargets", LookMode.Value, LookMode.Value);
        WorkTargets ??= new();
        foreach (string work in WorkTargets.Keys.ToArray()) WorkTargets[work] = Math.Clamp(WorkTargets[work], -1, 10);
        WorkUpdateHours = Math.Clamp(WorkUpdateHours, 1, 24);
        Hysteresis = Math.Clamp(Hysteresis, 0f, 20f);
    }
}

public sealed class AutonomousColonistsMod : Mod
{
    internal static AutonomousSettings? Settings;
    private static readonly AutonomyProfile[] Profiles = [AutonomyProfile.Conservative, AutonomyProfile.Balanced, AutonomyProfile.Advanced];
    private static readonly (ActionModules Module, string Label)[] Modules =
        [(ActionModules.Food, "Alimentación"), (ActionModules.Rest, "Descanso"), (ActionModules.Health, "Cuidados médicos"),
         (ActionModules.Apparel, "Ropa"), (ActionModules.Safety, "Huida, fuego propio y rescate")];
    private Vector2 scroll;
    private bool showCoverage;
    private bool confirmLocks;
    private bool showColonists = true;
    private float contentHeight = 1000f;

    public AutonomousColonistsMod(ModContentPack content) : base(content) => Settings = GetSettings<AutonomousSettings>();
    public override string SettingsCategory() => "Autonomous Colonists";

    public override void DoSettingsWindowContents(Rect rect)
    {
        AutonomousSettings? settings = Settings;
        if (settings == null) return;
        Map? currentMap = Find.CurrentMap;
        AutonomyGameComponent? director = Current.Game?.GetComponent<AutonomyGameComponent>();
        AutonomousCoordinatorComponent? coordinator = Current.Game?.GetComponent<AutonomousCoordinatorComponent>();
        var colonists = currentMap?.mapPawns.FreeColonistsSpawned.OrderBy(p => p.LabelShort).ToList();
        Rect contents = new(0f, 0f, Math.Max(100f, rect.width - 22f), Math.Max(rect.height, contentHeight));
        bool changed = false;
        Widgets.BeginScrollView(rect, ref scroll, contents);
        var listing = new Listing_Standard();
        listing.Begin(contents);
        try
        {
            listing.Label("Autonomous Colonists 0.5 experimental — acciones automáticas con políticas del juego.");
            listing.Label("Respeta cambios manuales observables y trabajos desactivados (0). Activa prioridades numéricas en Trabajo.");
            listing.GapLine();
            listing.Label("Autonomous Director — configuración de esta partida");
            if (coordinator != null)
            {
                bool enabled = coordinator.Enabled;
                listing.CheckboxLabeled("Activar Autonomous Director (voluntario)", ref enabled, height: 40f);
                if (enabled != coordinator.Enabled) coordinator.SetEnabled(enabled);
                foreach (AutonomyProfile mode in Profiles)
                {
                    string label = mode == AutonomyProfile.Conservative ? "Conservador" : mode == AutonomyProfile.Balanced ? "Equilibrado" : "Avanzado";
                    if (Widgets.ButtonText(listing.GetRect(40f), (coordinator.Profile == mode ? "● " : "○ ") + label)) coordinator.SetProfile(mode);
                }
                bool observe = coordinator.ObservationOnly;
                listing.CheckboxLabeled("Modo observación (sin escrituras)", ref observe, height: 40f);
                if (observe != coordinator.ObservationOnly) coordinator.SetObservation(observe);
                listing.Label("La observación activa suspende también cambios y restauraciones de prioridades.");
                bool actions = coordinator.ActionsEnabled;
                listing.CheckboxLabeled("Ejecutar acciones automáticas (experimental)", ref actions, height: 48f);
                if (actions != coordinator.ActionsEnabled) coordinator.SetActions(actions);
                listing.Label("Requiere director activo y observación desmarcada. Al actualizar desde v0.4, las acciones empiezan apagadas.");
                foreach (var module in Modules)
                {
                    bool allowed = coordinator.ModuleEnabled(module.Module);
                    bool before = allowed;
                    listing.CheckboxLabeled(module.Label, ref allowed, height: 40f);
                    if (allowed != before) coordinator.SetModule(module.Module, allowed);
                }
                listing.Label("Autocuidado solo con permiso del juego. La huida ante enemigos requiere respuesta Huir. No hay defensa armada automática ni cambios de zonas/equipo militar.");
                listing.Label("Los trabajos se activan por separado abajo. Nunca se cancelan órdenes manuales ni se cambian horarios/políticas.");
                listing.Label(coordinator.IntervalOverride == 0 ? "Intervalo: automático según perfil" : $"Intervalo: {coordinator.IntervalOverride} ticks");
                if (Widgets.ButtonText(listing.GetRect(40f), "Usar intervalo del perfil")) coordinator.SetInterval(0);
                int cadence = coordinator.IntervalOverride == 0 ? DirectorPolicy.For(coordinator.Profile).Interval : coordinator.IntervalOverride;
                if (Widgets.ButtonText(listing.GetRect(40f), "Reducir intervalo en 250 ticks")) coordinator.SetInterval(Math.Max(250, cadence - 250));
                if (Widgets.ButtonText(listing.GetRect(40f), "Aumentar intervalo en 250 ticks")) coordinator.SetInterval(Math.Min(2500, cadence + 250));
                listing.Label(coordinator.Status);
                listing.Label($"Última evaluación: {(coordinator.LastEvaluation < 0 ? "pendiente" : coordinator.LastEvaluation.ToString())} ticks. {coordinator.LastMilliseconds:F2} ms; {coordinator.LastAllocatedBytes} bytes asignados.");
                listing.Label($"Propuestas en esta sesión: {coordinator.ProposedCount}. Ejecutadas: {coordinator.ExecutedCount}.");
                listing.Label($"Búsquedas de acciones en la última evaluación: {coordinator.LastActionProbes}/4.");
                listing.Label("Último motivo: " + coordinator.LastReason);
                if (Widgets.ButtonText(listing.GetRect(40f), "Evaluar propuestas ahora")) coordinator.EvaluateNow();
            }
            else listing.Label("Abre una partida. Director apagado y perfil Equilibrado por defecto.");
            listing.GapLine();
            listing.Label("Gestor de prioridades v0.3 — permisos independientes");
            if (AutonomyGameComponent.WorkManagerConflict()) listing.Label("Work Manager activo: gestor suspendido.");
            if (director != null)
            {
                bool enabled = director.EnabledForSave;
                listing.CheckboxLabeled("Automatizar prioridades en esta partida", ref enabled);
                if (enabled != director.EnabledForSave) director.SetEnabled(enabled);
            }
            else listing.Label("Abre una partida para activar la automatización (apagada por defecto en cada partida).");
            listing.Label("Frecuencia: " + settings.WorkUpdateHours + " horas del juego");
            int hours = (int)listing.Slider(settings.WorkUpdateHours, 1, 24);
            changed |= hours != settings.WorkUpdateHours;
            settings.WorkUpdateHours = hours;
            listing.Label("Estabilidad de especialidades: " + settings.Hysteresis.ToString("F0") + " puntos de aptitud");
            float stability = (float)Math.Round(listing.Slider(settings.Hysteresis, 0f, 20f));
            changed |= stability != settings.Hysteresis;
            settings.Hysteresis = stability;
            if (listing.ButtonText("Recalcular ahora")) director?.RecalculateNow();
            if (director != null)
            {
                listing.Label($"{director.Status}. Último cálculo: {director.LastMilliseconds:F2} ms, {director.LastAssignments} celdas, {director.LastWrites} cambios.");
                listing.Label("Cobertura urgente: " + director.CoverageWarnings);
            }
            listing.GapLine();
            listing.CheckboxLabeled("Mostrar cobertura por trabajo", ref showCoverage);
            if (showCoverage)
            {
                listing.Label("-1: cobertura según población; 0: no gestionar; 1–10: especialistas deseados. Solo usa trabajos ya habilitados.");
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(w => w.defName, StringComparer.Ordinal))
                {
                    int target = settings.WorkTargets.GetValueOrDefault(work.defName, -1);
                    if (!WorkAllocator.UsesCoverage(work.defName, work.relevantSkills?.Count > 0))
                    {
                        bool manage = target != 0, before = manage;
                        listing.CheckboxLabeled("Gestionar: " + work.LabelCap, ref manage);
                        if (manage != before) { settings.WorkTargets[work.defName] = manage ? -1 : 0; changed = true; }
                        continue;
                    }
                    listing.Label(work.LabelCap + ": " + (target < 0 ? "automático" : target == 0 ? "no gestionar" : target.ToString()));
                    int next = (int)Math.Round(listing.Slider(target, -1, 10));
                    if (target != next) { settings.WorkTargets[work.defName] = next; changed = true; }
                }
            }
            listing.GapLine();
            listing.CheckboxLabeled("Mostrar colonos del mapa actual", ref showColonists);
            if (showColonists && director != null && colonists != null)
            {
                listing.Label("Las exclusiones siguen a cada colono entre mapas y se guardan en esta partida.");
                foreach (Pawn pawn in colonists)
                {
                    bool excluded = director.IsExcluded(pawn), next = excluded;
                    listing.CheckboxLabeled("Excluir de ambos sistemas: " + pawn.LabelShort, ref next, height: 40f);
                    if (next != excluded) director.SetExcluded(pawn, next);
                }
            }
            else if (currentMap == null) listing.Label("Abre una partida para gestionar colonos.");
            listing.GapLine();
            listing.CheckboxLabeled("Permitir gestionar de nuevo mis celdas bloqueadas", ref confirmLocks);
            if (confirmLocks && listing.ButtonText("Liberar bloqueos manuales de esta partida"))
            {
                director?.ReleaseManualLocks();
                confirmLocks = false;
            }
            listing.Label("Desactiva el gestor antes de retirar el mod y guarda. Solo se restauran valores que aún controla el mod.");
        }
        finally
        {
            contentHeight = listing.CurHeight + 24f;
            listing.End();
            Widgets.EndScrollView();
        }
        if (changed) director?.SettingsChanged();
    }
}
