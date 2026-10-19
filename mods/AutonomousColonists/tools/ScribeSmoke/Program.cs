using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
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
    Scribe.saver.InitSaving(args[1], "autonomousSmoke");
    Scribe_Deep.Look(ref original, "director", new object[] { null! });
    Scribe.saver.FinalizeSaving();
    saved = true;
    AutonomyGameComponent loaded = null!;
    Scribe.loader.InitLoading(args[1]);
    Scribe_Deep.Look(ref loaded, "director", new object[] { null! });
    Scribe.loader.FinalizeLoading();
    if (loaded == null || !loaded.EnabledForSave ||
        Field<Dictionary<string, int>>(loaded, "originals")["Pawn1|Doctor"] != 3 ||
        Field<Dictionary<string, int>>(loaded, "applied")["Pawn1|Doctor"] != 1 ||
        Field<Dictionary<string, int>>(loaded, "seen")["Pawn1|Doctor"] != 1 ||
        !Field<List<string>>(loaded, "manualLocks").Contains("Pawn2|Cooking") ||
        !Field<List<string>>(loaded, "exclusions").Contains("Pawn2") ||
        !Field<HashSet<string>>(loaded, "recovering").Contains("Pawn1"))
        throw new InvalidOperationException("Verse.Scribe round-trip state mismatch");
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
    scope = "Real Verse.Scribe component with synthetic IDs and no pawn references; not a game save or Android test",
    gameExecuted = false, androidExecuted = false
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(passed ? "PASS isolated Verse.Scribe round trip" : "BLOCKED/FAILED isolated Verse.Scribe: " + errorMessage);
foreach (string assembly in missing.Keys.OrderBy(s => s, StringComparer.Ordinal)) Console.WriteLine("Missing runtime assembly: " + assembly);
return passed ? 0 : 1;

static T Field<T>(object target, string name) =>
    (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
