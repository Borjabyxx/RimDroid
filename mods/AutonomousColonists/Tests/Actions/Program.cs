using System.Diagnostics;
using System.Text.Json;
using AutonomousColonists.Core;

static class Suite
{
    static void Check(bool value, string message = "invariant failed") { if (!value) throw new Exception(message); }
    static DecisionCandidate C(string pawn = "A", ProposedAction action = ProposedAction.Eat, Urgency urgency = Urgency.Important) =>
        new(pawn, 1, NeedKind.Food, 600, urgency, "test", ModuleKind.Needs, action, DecisionConditions.AlternativeKnown,
            DecisionConditions.None, Authorization.AwaitingValidation);
    static void Run(ExecutionCoordinator e, Runtime r, IReadOnlyList<DecisionCandidate>? c = null, int tick = 0,
        bool enabled = true, bool observation = false, bool actions = true, AutonomyProfile profile = AutonomyProfile.Balanced,
        Action<DecisionCandidate, Authorization, string>? report = null) =>
        e.Run(c ?? [C()], tick, enabled, observation, actions, profile, r, report ?? ((_, _, _) => { }));
    static AutonomousDecisionCoordinator Director() => new() { Enabled = true, ObservationOnly = false, ActionsEnabled = true };
    static PawnObservation Pawn(string id = "A") => new(id, 1, .2f, .9f, 1);
    static void Pipeline(AutonomousDecisionCoordinator d, ExecutionCoordinator e, Runtime r, PawnObservation pawn) =>
        Run(e, r, d.Evaluate([pawn], 0, true), report: d.RecordOutcome);
    public static int Main(string[] args)
    {
        (string Name, Action Run)[] tests =
        [
            ("execution remains opt-in on upgrade", () => { var d = new AutonomousDecisionCoordinator(); Check(!d.ActionsEnabled && !d.Enabled && d.Profile == AutonomyProfile.Balanced); }),
            ("observation never inspects or invokes finders", () => { var r = new Runtime(); Run(new(), r, observation: true); Check(r.Inspections == 0 && r.Prepares == 0 && r.Starts == 0); }),
            ("disabled director never invokes runtime", () => { var r = new Runtime(); Run(new(), r, enabled: false); Check(r.Inspections == 0 && r.Prepares == 0); }),
            ("disabled execution never invokes runtime", () => { var r = new Runtime(); Run(new(), r, actions: false); Check(r.Inspections == 0 && r.Prepares == 0); }),
            ("food pipeline reports only confirmed execution", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn()); Check(r.Starts == 1 && d.ExecutedCount == 1 && d.Journal.Last().Authorization == Authorization.Executed); }),
            ("rest pipeline runs only one conflicting activity", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn() with { Food = .4f, Rest = .02f }); Check(r.Actions.SequenceEqual([ProposedAction.Rest])); }),
            ("medical emergency outranks important needs", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn() with { CanDoctor = true, PatientsNeedCare = true, UrgentPatient = true }); Check(r.Actions[0] == ProposedAction.TendPatient); }),
            ("selfcare requires both permissions in model", () => { var d = Director(); var p = Pawn() with { Food = .9f, NeedsTend = true, CanDoctor = true }; Check(d.Evaluate([p], 0).Count == 0); Check(d.Evaluate([p with { SelfTendAllowed = true }], 1500, true).First().Action == ProposedAction.SelfTend); }),
            ("medical rest pipeline", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn() with { Food = .9f, MedicalRestNeeded = true }); Check(r.Actions.SequenceEqual([ProposedAction.MedicalRest])); }),
            ("apparel pipeline does not invent garment availability", () => { var d = Director(); var r = new Runtime { NoJob = true }; Pipeline(d, new(), r, Pawn() with { Food = .9f, ApparelReviewDue = true }); Check(r.Prepares == 1 && r.Starts == 0 && d.ExecutedCount == 0); }),
            ("apparel confirmed action pipeline", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn() with { Food = .9f, ApparelReviewDue = true }); Check(r.Actions.SequenceEqual([ProposedAction.OptimizeApparel])); }),
            ("rescue pipeline", () => { var d = Director(); var r = new Runtime(); Pipeline(d, new(), r, Pawn() with { Food = .9f, RescueNeeded = true, CanRescue = true }); Check(r.Actions.SequenceEqual([ProposedAction.Rescue])); }),
            ("safety preempts resource searches in execution mode", () => { var d = Director(); var r = new Runtime { Facts = new(1, Threat: true) }; Pipeline(d, new(), r, Pawn() with { Food = .01f, Threat = true }); Check(r.Actions.SequenceEqual([ProposedAction.SeekSafety])); }),
            ("all manual vetoes prevent native preparation", () => { foreach (var facts in new[] { new ExecutionFacts(1, Manual: true), new(1, Excluded: true), new(1, Unavailable: true), new(1, Duty: true), new(1, Carrying: true), new(1, Queued: true), new(1, ProtectedJob: true) }) { var r = new Runtime { Facts = facts }; Run(new(), r); Check(r.Prepares == 0); } }),
            ("changed map vetoes preparation", () => { var r = new Runtime { Facts = new(2) }; Run(new(), r); Check(r.Prepares == 0); }),
            ("schedule vetoes preparation", () => { var r = new Runtime { Facts = new(1, ScheduleAllowed: false) }; Run(new(), r, [C(action: ProposedAction.Rest)]); Check(r.Prepares == 0); }),
            ("module vetoes preparation", () => { var r = new Runtime { Facts = new(1, ModuleAllowed: false) }; Run(new(), r); Check(r.Prepares == 0); }),
            ("danger prevents non-safety searches", () => { var r = new Runtime { Facts = new(1, Threat: true) }; Run(new(), r, [C(action: ProposedAction.TendPatient, urgency: Urgency.Critical)]); Check(r.Prepares == 0); }),
            ("normal need never interrupts busy work", () => { var r = new Runtime { Facts = new(1, Idle: false, SuspendableWork: true) }; Run(new(), r); Check(r.Prepares == 0); }),
            ("critical may suspend ordinary resumable work", () => { var r = new Runtime { Facts = new(1, Idle: false, SuspendableWork: true) }; Run(new(), r, [C(urgency: Urgency.Critical)]); Check(r.Starts == 1); }),
            ("critical never interrupts protected work", () => { var r = new Runtime { Facts = new(1, Idle: false, SuspendableWork: true, ProtectedJob: true) }; Run(new(), r, [C(urgency: Urgency.Critical)]); Check(r.Prepares == 0); }),
            ("manual order appearing during finder cancels action", () => { var r = new Runtime(); r.BeforePrepare = () => r.Facts = new(1, Manual: true); Run(new(), r); Check(r.Starts == 0 && r.Disposals == 1); }),
            ("stale policy target or reservation cancels action", () => { var r = new Runtime { Valid = false }; var e = new ExecutionCoordinator(); Run(e, r); Check(r.Starts == 0 && r.Disposals == 1 && e.ExecutedCount == 0); }),
            ("failed native start never counts", () => { var r = new Runtime { StartSucceeds = false }; var e = new ExecutionCoordinator(); Run(e, r); Check(r.Starts == 1 && r.Disposals == 1 && e.ExecutedCount == 0); }),
            ("prepared jobs disposed after successful transfer", () => { var r = new Runtime(); Run(new(), r); Check(r.Starts == 1 && r.Disposals == 1); }),
            ("same target cannot be assigned twice in batch", () => { var r = new Runtime { SharedTarget = true }; Run(new(), r, [C("A"), C("B")]); Check(r.Starts == 1 && r.Disposals == 2); }),
            ("fallback when higher priority has no alternative", () => { var r = new Runtime { MissingAction = ProposedAction.Rest }; var a = C(action: ProposedAction.Rest); var b = C() with { Authorization = Authorization.Conflict }; Run(new(), r, [a, b]); Check(r.Actions.SequenceEqual([ProposedAction.Eat]) && r.Prepares == 2); }),
            ("fallback never bypasses manual order", () => { var r = new Runtime { Facts = new(1, Manual: true) }; Run(new(), r, [C(), C(action: ProposedAction.Rest) with { Authorization = Authorization.Conflict }]); Check(r.Prepares == 0); }),
            ("stability veto cannot authorize lower conflict", () => { var r = new Runtime(); Run(new(), r, [C() with { Authorization = Authorization.Stability }, C(action: ProposedAction.Rest) with { Authorization = Authorization.Conflict }]); Check(r.Prepares == 0); }),
            ("confirmed fallback becomes incumbent", () => { var d = Director(); var r = new Runtime { MissingAction = ProposedAction.TendPatient }; Pipeline(d, new(), r, Pawn() with { CanDoctor = true, PatientsNeedCare = true }); Check(d.ExportMemory()[0].LastAction == ProposedAction.Eat && d.ExecutedCount == 1); }),
            ("profile action limits", () => { foreach (var (profile, count) in new[] { (AutonomyProfile.Conservative, 1), (AutonomyProfile.Balanced, 2), (AutonomyProfile.Advanced, 3) }) { var r = new Runtime(); Run(new(), r, Enumerable.Range(0, 20).Select(i => C("P" + i)).ToArray(), profile: profile); Check(r.Starts == count && r.Prepares == count); } }),
            ("failed searches bounded to four probes", () => { var e = new ExecutionCoordinator(); var r = new Runtime { NoJob = true }; Run(e, r, Enumerable.Range(0, 50).Select(i => C("P" + i)).ToArray()); Check(r.Prepares == 4 && e.LastProbes == 4); }),
            ("fair rotation across fifty pawns", () => { var e = new ExecutionCoordinator(); var r = new Runtime(); var c = Enumerable.Range(0, 50).Select(i => C("P" + i)).ToArray(); for (int i = 0; i < 25; i++) Run(e, r, c, i * 1500); Check(r.Pawns.Count == 50); }),
            ("per pawn cooldown across different modules", () => { var e = new ExecutionCoordinator(); var r = new Runtime(); Run(e, r); Run(e, r, [C(action: ProposedAction.Rest)], 1000); Check(r.Starts == 1); Run(e, r, [C(action: ProposedAction.Rest)], 1500); Check(r.Starts == 2); }),
            ("failed food search backoff", () => { var e = new ExecutionCoordinator(); var r = new Runtime { NoJob = true }; Run(e, r); Run(e, r, tick: 500); Check(r.Prepares == 1); Run(e, r, tick: 1000); Check(r.Prepares == 2); }),
            ("apparel search backoff is longer", () => { var e = new ExecutionCoordinator(); var r = new Runtime { NoJob = true }; Run(e, r, [C(action: ProposedAction.OptimizeApparel)]); Run(e, r, [C(action: ProposedAction.OptimizeApparel)], 4000); Check(r.Prepares == 1); }),
            ("execution cooldown value save roundtrip", () => { var e = new ExecutionCoordinator(); Run(e, new(), tick: 1000); var saved = JsonSerializer.Deserialize<ExecutionMemory[]>(JsonSerializer.Serialize(e.ExportMemory()))!; var loaded = new ExecutionCoordinator(); loaded.ImportMemory(saved); var r = new Runtime(); Run(loaded, r, tick: 1500); Check(r.Prepares == 0); }),
            ("map transfer invalidates execution cooldown", () => { var e = new ExecutionCoordinator(); Run(e, new(), tick: 1000); var r = new Runtime { Facts = new(2) }; Run(e, r, [C() with { MapId = 2 }], 1100); Check(r.Starts == 1); }),
            ("tick rollback cannot permanently lock actors", () => { var e = new ExecutionCoordinator(); Run(e, new(), tick: 5000); var r = new Runtime(); Run(e, r, tick: 0); Check(r.Starts == 1); }),
            ("excluded and departed memory pruned", () => { var e = new ExecutionCoordinator(); Run(e, new()); e.Retain([Pawn() with { Excluded = true }]); Check(e.ExportMemory().Length == 0); }),
            ("disabled module candidates do not block others", () => { var d = Director(); d.AllowedModules = ActionModules.Food; var result = d.Evaluate([Pawn() with { Rest = .01f, CanDoctor = true, PatientsNeedCare = true, ApparelReviewDue = true }], 0); Check(result.All(c => c.Action == ProposedAction.Eat)); }),
            ("observation overrides all action flags", () => { var d = Director(); d.ObservationOnly = true; var e = new ExecutionCoordinator(); var r = new Runtime(); Run(e, r, d.Evaluate([Pawn() with { CanDoctor = true, PatientsNeedCare = true }], 0), observation: true); Check(r.Prepares == 0 && d.ExecutedCount == 0); }),
            ("exceptions propagate for game component fail-closed latch", () => { var r = new Runtime { Throw = true }; bool thrown = false; try { Run(new(), r); } catch (InvalidOperationException) { thrown = true; } Check(thrown && r.Starts == 0); }),
            ("unsupported action cannot reach runtime", () => { var r = new Runtime(); Run(new(), r, [C(action: ProposedAction.ReviewWorkAllocation)]); Check(r.Prepares == 0); }),
            ("randomized authorization and execution invariants", Fuzz)
        ];
        var results = new List<object>(); int failures = 0;
        foreach (var t in tests)
            try { t.Run(); results.Add(new { name = t.Name, passed = true, error = (string?)null }); Console.WriteLine("PASS " + t.Name); }
            catch (Exception error) { failures++; results.Add(new { name = t.Name, passed = false, error = error.Message }); Console.WriteLine("FAIL " + t.Name + ": " + error); }
        var benchmarks = Benchmarks();
        if (args.Length == 2 && args[0] == "--report")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { tests = results, failures, benchmarks,
                scope = "Pure dispatcher/module integration with recording test doubles; native Verse actions NOT executed", gameExecuted = false, androidExecuted = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} action tests passed; native game/Android pending.");
        return failures == 0 ? 0 : 1;
    }
    sealed class Runtime : IActionRuntime
    {
        public ExecutionFacts Facts = new(1);
        public int Inspections, Prepares, Starts, Disposals;
        public bool NoJob, Valid = true, StartSucceeds = true, SharedTarget, Throw;
        public ProposedAction MissingAction;
        public Action? BeforePrepare;
        public readonly List<ProposedAction> Actions = new();
        public readonly HashSet<string> Pawns = new();
        public ExecutionFacts Inspect(DecisionCandidate c) { Inspections++; return Facts; }
        public IPreparedAction? Prepare(DecisionCandidate c)
        {
            Prepares++; BeforePrepare?.Invoke();
            if (Throw) throw new InvalidOperationException("injected finder error");
            return NoJob || c.Action == MissingAction ? null : new Prepared(this, c);
        }
        sealed class Prepared(Runtime r, DecisionCandidate c) : IPreparedAction
        {
            public string TargetKey => r.SharedTarget ? "shared" : c.PawnId;
            public bool Revalidate() => r.Valid;
            public bool TryStart() { r.Starts++; if (r.StartSucceeds) { r.Actions.Add(c.Action); r.Pawns.Add(c.PawnId); } return r.StartSucceeds; }
            public void Dispose() => r.Disposals++;
        }
    }
    static void Fuzz()
    {
        var random = new Random(14509);
        for (int scenario = 0; scenario < 1000; scenario++)
        {
            bool manual = random.Next(4) == 0, observe = random.Next(3) == 0;
            bool excluded = random.Next(4) == 0, threat = random.Next(3) == 0;
            var action = (ProposedAction)new[] { 1, 2, 3, 6, 7, 8, 9, 10 }[random.Next(8)];
            var r = new Runtime { Facts = new(1, Manual: manual, Excluded: excluded, Threat: threat) };
            var e = new ExecutionCoordinator();
            Run(e, r, [C(action: action)], observation: observe);
            if (manual || excluded || observe || threat && action != ProposedAction.SeekSafety) Check(r.Prepares == 0);
            Check(e.ExecutedCount <= 1 && e.ExecutedCount == r.Actions.Count && r.Disposals <= r.Prepares);
        }
    }
    static List<object> Benchmarks()
    {
        var result = new List<object>();
        foreach (int n in new[] { 5, 20, 50 })
        {
            var pawns = Enumerable.Range(0, n).Select(i => Pawn("P" + i) with { Rest = .2f, CanDoctor = i % 5 == 0, PatientsNeedCare = true, ApparelReviewDue = true }).ToArray();
            var d = Director(); var e = new ExecutionCoordinator(); var r = new Runtime { NoJob = true };
            for (int i = 0; i < 100; i++) Run(e, r, d.Evaluate(pawns, i * 1500, true), i * 1500);
            var samples = new double[500]; long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++) { long start = Stopwatch.GetTimestamp(); int tick = (i + 100) * 1500;
                Run(e, r, d.Evaluate(pawns, tick, true), tick); samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes; Array.Sort(samples);
            Console.WriteLine($"BENCH pure modules+dispatcher {n} pawns median={samples[250]:F4}ms p95={samples[475]:F4}ms bytes/call={bytes / 500}");
            result.Add(new { pawns = n, samples = 500, medianMs = samples[250], p95Ms = samples[475], bytesPerEvaluation = bytes / 500,
                scope = "Linux x64 pure core plus recording runtime; EXCLUDES native finders, paths, jobs, Verse, UI and Android" });
        }
        return result;
    }
}
