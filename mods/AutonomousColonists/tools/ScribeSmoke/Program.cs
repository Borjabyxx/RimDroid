using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using AutonomousColonists;
using Verse;

if (args.Length != 3) throw new ArgumentException("Private references directory, temporary XML, JSON output required");
var missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(args[0], name.Name + ".dll");
    if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    missing.TryAdd(name.FullName, 0);
    return null;
};
AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
{
    if (e.Exception is ReflectionTypeLoadException types)
        foreach (Exception? error in types.LoaderExceptions)
            if (error is FileNotFoundException file && file.FileName != null) missing.TryAdd(file.FileName, 0);
};
bool passed = false, saved = false;
string? errorMessage = null;
try
{
    var original = new AutonomyGameComponent(null!);
    Field<Dictionary<string, int>>(original, "originals").Add("Pawn1|Doctor", 3);
    Field<Dictionary<string, int>>(original, "applied").Add("Pawn1|Doctor", 1);
    Field<Dictionary<string, int>>(original, "seen").Add("Pawn1|Doctor", 1);
    Field<List<string>>(original, "manualLocks").Add("Pawn2|Cooking");
    Field<List<string>>(original, "exclusions").Add("Pawn2");
    Field<HashSet<string>>(original, "recovering").Add("Pawn1");
    original.GetType().GetField("enabledForSave", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(original, true);
    var coordinator = new AutonomousCoordinatorComponent(null!);
    coordinator.SetEnabled(true);
    coordinator.SetObservation(false);
    coordinator.SetInterval(1000);
    coordinator.SetActions(true);
    object execution = Field<object>(coordinator, "execution");
    Type executionType = typeof(AutonomousCoordinatorComponent).Assembly.GetType("AutonomousColonists.Core.ExecutionMemory", true)!;
    var executions = Array.CreateInstance(executionType, 1);
    executions.SetValue(Activator.CreateInstance(executionType, "Pawn1", 1, 450), 0);
    execution.GetType().GetMethod("ImportMemory")!.Invoke(execution, new object[] { executions });
    object core = Field<object>(coordinator, "core");
    Assembly mod = typeof(AutonomousCoordinatorComponent).Assembly;
    Type actorType = mod.GetType("AutonomousColonists.Core.ActorMemory", true)!;
    Type urgencyType = mod.GetType("AutonomousColonists.Core.Urgency", true)!;
    Type actionType = mod.GetType("AutonomousColonists.Core.ProposedAction", true)!;
    var memories = Array.CreateInstance(actorType, 1);
    memories.SetValue(Activator.CreateInstance(actorType, "Pawn1", 1, Enum.ToObject(urgencyType, 3),
        Enum.ToObject(urgencyType, 0), Enum.ToObject(actionType, 1), 900, Enum.ToObject(urgencyType, 3), 500), 0);
    core.GetType().GetMethod("ImportMemory")!.Invoke(core, new object[] { memories });
    Scribe.saver.InitSaving(args[1], "autonomousSmoke");
    Scribe_Deep.Look(ref original, "director", new object[] { null! });
    Scribe_Deep.Look(ref coordinator, "coordinator", new object[] { null! });
    Scribe.saver.FinalizeSaving();
    var xml = XDocument.Load(args[1]);
    if (!xml.Descendants("directorMemory").Any() || !xml.Descendants("directorExecutionMemory").Any() ||
        !xml.Descendants("directorActionsEnabled").Any(e => e.Value == "True" || e.Value == "true") ||
        !xml.Descendants("directorEnabled").Any(e => e.Value == "True" || e.Value == "true"))
        throw new InvalidOperationException("Director fields absent from saved XML");
    saved = true;
    AutonomyGameComponent loaded = null!;
    AutonomousCoordinatorComponent loadedCoordinator = null!;
    Scribe.loader.InitLoading(args[1]);
    Scribe_Deep.Look(ref loaded, "director", new object[] { null! });
    Scribe_Deep.Look(ref loadedCoordinator, "coordinator", new object[] { null! });
    Scribe.loader.FinalizeLoading();
    if (loaded == null || !loaded.EnabledForSave ||
        Field<Dictionary<string, int>>(loaded, "originals")["Pawn1|Doctor"] != 3 ||
        Field<Dictionary<string, int>>(loaded, "applied")["Pawn1|Doctor"] != 1 ||
        Field<Dictionary<string, int>>(loaded, "seen")["Pawn1|Doctor"] != 1 ||
        !Field<List<string>>(loaded, "manualLocks").Contains("Pawn2|Cooking") ||
        !Field<List<string>>(loaded, "exclusions").Contains("Pawn2") ||
        !Field<HashSet<string>>(loaded, "recovering").Contains("Pawn1"))
        throw new InvalidOperationException("Verse.Scribe round-trip state mismatch");
    if (loadedCoordinator == null || !loadedCoordinator.Enabled || loadedCoordinator.ObservationOnly || !loadedCoordinator.ActionsEnabled ||
        loadedCoordinator.IntervalOverride != 1000 ||
        ((Array)Field<object>(loadedCoordinator, "core").GetType().GetMethod("ExportMemory")!
            .Invoke(Field<object>(loadedCoordinator, "core"), null)!).Length != 1)
        throw new InvalidOperationException("Director Verse.Scribe round-trip state mismatch");
    if (((Array)Field<object>(loadedCoordinator, "execution").GetType().GetMethod("ExportMemory")!
        .Invoke(Field<object>(loadedCoordinator, "execution"), null)!).Length != 1)
        throw new InvalidOperationException("Execution cooldown Verse.Scribe round-trip state mismatch");
    passed = true;
}
catch (Exception ex)
{
    errorMessage = ex.GetType().Name + ": " + ex.Message;
    if (ex is FileNotFoundException file && file.FileName != null) missing.TryAdd(file.FileName, 0);
}
File.WriteAllText(args[2], JsonSerializer.Serialize(new
{
    passed, xmlSaved = saved, missingAssemblies = missing.Keys.OrderBy(s => s, StringComparer.Ordinal), error = errorMessage,
    scope = "Real Verse.Scribe for work ledger, director and v0.5 execution permission/cooldown with synthetic IDs; not a game save or Android test",
    gameExecuted = false, androidExecuted = false
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(passed ? "PASS isolated Verse.Scribe round trip" : "BLOCKED/FAILED isolated Verse.Scribe: " + errorMessage);
foreach (string assembly in missing.Keys.OrderBy(s => s, StringComparer.Ordinal)) Console.WriteLine("Missing runtime assembly: " + assembly);
return passed ? 0 : 1;

static T Field<T>(object target, string name) =>
    (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
