using System.Diagnostics;
using System.Text.Json;
using AutonomousColonists.Core;

static class Suite
{
    static void Check(bool condition, string message = "invariant failed") { if (!condition) throw new Exception(message); }
    static AutonomousDecisionCoordinator Director(bool observation = true) => new() { Enabled = true, ObservationOnly = observation };
    static PawnObservation Pawn(string id = "A", float? food = .2f, float? rest = .9f) => new(id, 1, food, rest, 1);
    static DecisionCandidate Winner(AutonomousDecisionCoordinator director, PawnObservation pawn, int tick = 0) =>
        director.Evaluate([pawn], tick, force: true).First();
    public static int Main(string[] args)
    {
        (string Name, Action Run)[] tests =
        [
            ("balanced default and opt-in", () => { var d = new AutonomousDecisionCoordinator(); Check(d.Profile == AutonomyProfile.Balanced && !d.Enabled && d.ObservationOnly); }),
            ("disabled director has no proposals or memory", () => { var d = new AutonomousDecisionCoordinator(); Check(d.Evaluate([Pawn()], 0, true).Count == 0 && d.ExportMemory().Length == 0); }),
            ("food important and rest normal are observed", () => { var d = Director(); Check(Winner(d, Pawn()).Need == NeedKind.Food && d.ExportMemory()[0].Rest == Urgency.Normal); }),
            ("rest critical outranks important food", () => Check(Winner(Director(), Pawn(rest: .02f)).Action == ProposedAction.Rest)),
            ("critical hunger outranks coarse map threat", () => Check(Winner(Director(), Pawn(food: .02f) with { Threat = true }).Need == NeedKind.Food)),
            ("immediate danger outranks critical needs", () => Check(Winner(Director(), Pawn(food: .02f) with { ImmediateDanger = true }).Need == NeedKind.Safety)),
            ("manual orders always veto even advanced critical", () => { var d = Director(false); d.Profile = AutonomyProfile.Advanced; Check(d.Evaluate([Pawn(food: .01f) with { ManualOrder = true, ImmediateDanger = true }], 0).All(c => c.Authorization == Authorization.ManualOrder)); }),
            ("drafted colonists veto", () => Check(Winner(Director(false), Pawn() with { Drafted = true }).Authorization == Authorization.ManualOrder)),
            ("unavailable colonists veto", () => Check(Winner(Director(false), Pawn() with { Unavailable = true }).Authorization == Authorization.Unavailable)),
            ("exclusions remove proposals and cached memory", () => { var d = Director(); Winner(d, Pawn()); Check(d.Evaluate([Pawn() with { Excluded = true }], 1000, true).Count == 0 && d.ExportMemory().Length == 0); }),
            ("one activity winner across modules", () => { var a = Director().Evaluate([Pawn() with { Threat = true }], 0); Check(a.Count(c => c.Authorization != Authorization.Conflict) == 1 && a.Any(c => c.Module == ModuleKind.Work)); }),
            ("module ties independent of registration order", () => {
                var a = new AutonomousDecisionCoordinator(new FixedModule(ModuleKind.Apparel), new FixedModule(ModuleKind.Health)) { Enabled = true };
                var b = new AutonomousDecisionCoordinator(new FixedModule(ModuleKind.Health), new FixedModule(ModuleKind.Apparel)) { Enabled = true };
                Check(a.Evaluate([Pawn()], 0).SequenceEqual(b.Evaluate([Pawn()], 0)) && a.Journal.First().Module == ModuleKind.Health);
            }),
            ("normal needs create no candidates", () => Check(Director().Evaluate([Pawn(food: .9f)], 0).Count == 0)),
            ("unknown and invalid needs are not fabricated", () => { foreach (float? v in new float?[] { null, float.NaN, float.PositiveInfinity, -.1f, 1.1f }) Check(Director().Evaluate([Pawn(food: v, rest: v)], 0).Count == 0); }),
            ("all four need levels", () => {
                var p = DirectorPolicy.For(AutonomyProfile.Balanced);
                foreach (var (level, expected) in new[] { (.8f, Urgency.Normal), (.4f, Urgency.Growing), (.2f, Urgency.Important), (.05f, Urgency.Critical) })
                    Check(DirectorPolicy.Classify(level, Urgency.Normal, p.FoodGrowing, p.FoodImportant, p.FoodCritical) == expected);
            }),
            ("conservative skips growing needs", () => { var d = Director(); d.Profile = AutonomyProfile.Conservative; Check(d.Evaluate([Pawn(food: .3f)], 0).Count == 0); }),
            ("advanced anticipates earlier", () => { var d = Director(); d.Profile = AutonomyProfile.Advanced; Check(Winner(d, Pawn(food: .5f)).Urgency == Urgency.Growing); Check(Director().Evaluate([Pawn(food: .5f)], 0).Count == 0); }),
            ("recovery hysteresis survives threshold noise", () => { var d = Director(); Winner(d, Pawn(food: .09f)); Check(Winner(d, Pawn(food: .12f), 750).Urgency == Urgency.Critical); Check(Winner(d, Pawn(food: .16f), 1500).Urgency == Urgency.Important); }),
            ("same proposal is held during stability window", () => { var d = Director(); Winner(d, Pawn()); Check(Winner(d, Pawn(), 750).Authorization == Authorization.Stability); }),
            ("critical escalation bypasses cooldown", () => { var d = Director(); Winner(d, Pawn()); Check(Winner(d, Pawn(food: .01f), 1).Authorization == Authorization.ObservationOnly); }),
            ("marginal task switch is held", () => { var d = Director(); Winner(d, Pawn()); Check(Winner(d, Pawn(food: .5f, rest: .2f), 750).Authorization == Authorization.Stability); Check(d.ExportMemory()[0].LastAction == ProposedAction.Eat); }),
            ("stable priority after cooldown can switch", () => { var d = Director(); Winner(d, Pawn()); Check(Winner(d, Pawn(food: .5f, rest: .2f), 1250).Action == ProposedAction.Rest && d.ExportMemory()[0].LastAction == ProposedAction.Rest); }),
            ("interruption cost discounts noncritical proposals", () => { var a = Winner(Director(), Pawn()); var b = Winner(Director(), Pawn() with { InterruptionCost = 100 }); Check(a.Priority - b.Priority == 100); Check(Winner(Director(), Pawn(food: .01f) with { InterruptionCost = 100 }).Priority == 900); }),
            ("alternatives unknown prevents authorization", () => Check(Winner(Director(false), Pawn()).Authorization == Authorization.MissingConditions)),
            ("known alternatives still cannot execute in v04", () => { var d = Director(false); Check(Winner(d, Pawn() with { FoodAvailable = true }).Authorization == Authorization.Unsupported && d.ExecutedCount == 0); }),
            ("observation mode has no execution capability", () => { var d = Director(); Check(Winner(d, Pawn() with { FoodAvailable = true }).Authorization == Authorization.ObservationOnly && d.ExecutedCount == 0); }),
            ("work writes gate includes restoration", () => {
                foreach (bool enabled in new[] { false, true }) foreach (bool observe in new[] { false, true })
                foreach (bool fault in new[] { false, true }) foreach (bool manual in new[] { false, true }) foreach (bool drafted in new[] { false, true })
                    Check(WorkAuthorization.CanWrite(enabled, observe, fault, manual, drafted) == (!manual && !drafted && !(enabled && (observe || fault))));
            }),
            ("deferred restoration retains ownership", () => { var l = new PriorityLedger(); l.Record("A|Doctor", 3, 1); if (WorkAuthorization.CanWrite(false, false, false, true, false)) l.Release("A|Doctor"); Check(l.HasOwnership("A") && !l.HasOwnership("AA") && l.RestoreValue("A|Doctor", 1) == 3); }),
            ("map change invalidates stability context", () => { var d = Director(); Winner(d, Pawn()); Check(Winner(d, Pawn() with { MapId = 2 }, 1).Authorization == Authorization.ObservationOnly && d.ExportMemory()[0].MapId == 2); }),
            ("departed colonists invalidate memory", () => { var d = Director(); Winner(d, Pawn()); d.Evaluate([], 1000, true); Check(d.ExportMemory().Length == 0); }),
            ("profile change invalidates threshold history", () => { var d = Director(); Winner(d, Pawn()); d.Profile = AutonomyProfile.Advanced; Check(d.ExportMemory().Length == 0 && d.Due(1)); }),
            ("invalid profile migrates to balanced", () => { var d = Director(); d.Profile = (AutonomyProfile)77; Check(d.Profile == AutonomyProfile.Balanced); }),
            ("missing saved state defaults preserve opt-in", () => { var d = new AutonomousDecisionCoordinator(); d.ImportMemory([]); Check(!d.Enabled && d.Profile == AutonomyProfile.Balanced && d.ObservationOnly && d.ExportMemory().Length == 0); }),
            ("save value roundtrip preserves hysteresis and stability", () => {
                var d = Director(); Winner(d, Pawn(food: .09f));
                var saved = JsonSerializer.Deserialize<ActorMemory[]>(JsonSerializer.Serialize(d.ExportMemory()))!;
                var loaded = Director(); loaded.ImportMemory(saved); var c = Winner(loaded, Pawn(food: .12f), 750);
                Check(c.Urgency == Urgency.Critical && c.Authorization == Authorization.Stability);
            }),
            ("invalid saved memory rejected", () => { var d = Director(); d.ImportMemory([new("", 1), new("A", 1, (Urgency)99), new("B", 1, LastDecisionTick: -3)]); Check(d.ExportMemory().Length == 0); }),
            ("tick rollback invalidates cooldown", () => { var d = Director(); Winner(d, Pawn(), 5000); Check(Winner(d, Pawn(), 10).Authorization == Authorization.ObservationOnly); }),
            ("disabled and reactivated retains safe state", () => { var d = Director(); Winner(d, Pawn()); d.Enabled = false; Check(d.Evaluate([Pawn()], 5000, true).Count == 0); d.ResetFault(); d.Enabled = true; Check(Winner(d, Pawn(), 5000).Authorization == Authorization.ObservationOnly); }),
            ("interval avoids redundant evaluations", () => { var d = Director(); Winner(d, Pawn()); Check(!d.Due(749) && d.Due(750)); Check(d.Evaluate([Pawn()], 100).Count == 0 && d.ProposedCount == 2); }),
            ("interval override clamps and profile policies differ", () => { var d = Director(); d.IntervalOverride = 1; Winner(d, Pawn()); Check(!d.Due(249) && d.Due(250)); d.IntervalOverride = 99999; Check(!d.Due(2499) && d.Due(2500)); }),
            ("zero allocation on not-due ticks", () => { var d = Director(); Winner(d, Pawn()); long bytes = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 10000; i++) Check(!d.Due(1)); Check(GC.GetAllocatedBytesForCurrentThread() - bytes == 0); }),
            ("emergency ended represented once", () => { var d = Director(); Winner(d, Pawn(food: .9f) with { Fire = true, NonCombatant = true }); var a = Winner(d, Pawn(food: .9f), 1000); Check(a.Action == ProposedAction.EmergencyEnded); Check(d.Evaluate([Pawn(food: .9f)], 2000, true).Count == 0); }),
            ("defense permission only observed", () => { var c = Winner(Director(), Pawn() with { Threat = true, DefenseAuthorized = true }); Check(c.Action == ProposedAction.SeekSafety && c.Reason.Contains("autorizada")); }),
            ("health observation used as context", () => Check(Winner(Director(), Pawn() with { Health = .3f }).Reason.Contains("salud reducida"))),
            ("module fault latches with no execution", () => { var d = new AutonomousDecisionCoordinator(new FaultModule()) { Enabled = true }; Check(d.Evaluate([Pawn()], 0).Count == 0 && d.Faulted && !d.Due(99999) && d.ExecutedCount == 0); }),
            ("recovery requires explicit reset", () => { var m = new FaultModule(); var d = new AutonomousDecisionCoordinator(m) { Enabled = true }; d.Evaluate([Pawn()], 0); m.Throw = false; Check(d.Evaluate([Pawn()], 1000).Count == 0 && d.Faulted); d.ResetFault(); d.Evaluate([Pawn()], 1000); Check(!d.Faulted && d.LastEvaluation == 1000); }),
            ("duplicate observations suspend safely", () => { var d = Director(); d.Evaluate([Pawn(), Pawn()], 0); Check(d.Faulted && d.ExecutedCount == 0); }),
            ("bounded journal and actor caches", () => { var d = Director(); for (int i = 0; i < 2000; i++) d.Evaluate([Pawn("P" + i)], i * 1000); Check(d.Journal.Count == 64 && d.ExportMemory().Length == 1); }),
            ("randomized safety invariants and permutation determinism", Fuzz),
            ("no constant switching on noisy scenarios", Noise),
            ("performance 5 20 50 scales without per-tick scans", () => { foreach (int n in new[] { 5, 20, 50 }) { var d = Director(); var pawns = Enumerable.Range(0, n).Select(i => Pawn("P" + i)).ToArray(); d.Evaluate(pawns, 0); Check(d.ExportMemory().Length == n && d.ProposedCount == n * 2 && !d.Due(1)); } })
        ];
        var results = new List<object>(); int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); results.Add(new { name = test.Name, passed = true, error = (string?)null }); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; results.Add(new { name = test.Name, passed = false, error = error.Message }); Console.WriteLine("FAIL " + test.Name + ": " + error); }
        }
        var benchmarks = Benchmarks();
        if (args.Length == 2 && args[0] == "--report")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { tests = results, failures, benchmarks, gameExecuted = false, androidExecuted = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} director tests passed. Verse/game/Android integration is separate.");
        return failures == 0 ? 0 : 1;
    }
    sealed class FixedModule(ModuleKind kind) : IAutonomyModule
    {
        public ModuleKind Kind => kind;
        public void Propose(in PawnObservation p, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates) =>
            candidates.Add(new(p.Id, p.MapId, NeedKind.Work, 500, Urgency.Important, "Tie", Kind, ProposedAction.ReviewWorkAllocation, DecisionConditions.None, DecisionConditions.None));
    }
    sealed class FaultModule : IAutonomyModule
    {
        public bool Throw = true;
        public ModuleKind Kind => ModuleKind.Health;
        public void Propose(in PawnObservation p, ActorMemory memory, DirectorPolicy policy, List<DecisionCandidate> candidates) { if (Throw) throw new InvalidOperationException("injected failure"); }
    }
    static void Fuzz()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            var random = new Random(seed);
            var pawns = Enumerable.Range(0, seed % 51).Select(i => Pawn("P" + i, random.NextSingle(), random.NextSingle()) with
            {
                MapId = random.Next(3), Drafted = random.Next(6) == 0, ManualOrder = random.Next(5) == 0,
                Unavailable = random.Next(10) == 0, Excluded = random.Next(10) == 0,
                Threat = random.Next(8) == 0, ImmediateDanger = random.Next(20) == 0, FoodAvailable = true, RestAvailable = true
            }).ToArray();
            var d = Director(seed % 2 == 0); var other = Director(seed % 2 == 0);
            d.Profile = other.Profile = (AutonomyProfile)(seed % 3);
            var a = d.Evaluate(pawns, 0).ToArray(); var b = other.Evaluate(pawns.Reverse().ToArray(), 0);
            Check(a.SequenceEqual(b), "permutation changed output");
            Check(d.ExecutedCount == 0 && !d.Faulted);
            foreach (var pawn in pawns)
            {
                var decisions = a.Where(c => c.PawnId == pawn.Id).ToArray();
                if (pawn.Excluded) Check(decisions.Length == 0);
                else if (pawn.ManualOrder || pawn.Drafted) Check(decisions.All(c => c.Authorization == Authorization.ManualOrder));
                else if (pawn.Unavailable) Check(decisions.All(c => c.Authorization == Authorization.Unavailable));
                else Check(decisions.Count(c => c.Authorization != Authorization.Conflict) <= 1);
            }
        }
    }
    static void Noise()
    {
        var d = Director(); var random = new Random(123); int changes = 0; ProposedAction previous = ProposedAction.None;
        for (int i = 0; i < 200; i++)
        {
            d.Evaluate([Pawn(food: .249f + random.NextSingle() * .004f, rest: .199f + random.NextSingle() * .004f)], i * 250, true);
            var action = d.ExportMemory()[0].LastAction;
            if (action != previous) changes++;
            previous = action;
        }
        Check(changes <= 2, "hysteresis did not stabilize");
    }
    static List<object> Benchmarks()
    {
        var result = new List<object>();
        foreach (int n in new[] { 5, 20, 50 })
        {
            var pawns = Enumerable.Range(0, n).Select(i => Pawn("P" + i.ToString("D3"), .2f, .2f) with { Threat = i % 5 == 0 }).ToArray();
            var director = Director();
            for (int i = 0; i < 100; i++) director.Evaluate(pawns, i * 750, true);
            var samples = new double[1000]; long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); director.Evaluate(pawns, (i + 100) * 750, true);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes; Array.Sort(samples);
            Console.WriteLine($"BENCH director Linux x64 {n} colonists median={samples[500]:F4}ms p95={samples[950]:F4}ms bytes/evaluation={bytes / 1000}");
            result.Add(new { pawns = n, samples = 1000, medianMs = samples[500], p95Ms = samples[950], bytesPerEvaluation = bytes / 1000,
                scope = "Pure proposal core, Linux x64, excludes Verse observation/Scribe/UI/Android. Not a device performance guarantee." });
        }
        return result;
    }
}
