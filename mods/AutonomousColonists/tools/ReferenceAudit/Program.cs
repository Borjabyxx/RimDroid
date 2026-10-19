using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Usage: ReferenceAudit <private refs dir> <mod DLL> <JSON report>");
object Inspect(string path, bool mod = false)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null) throw new InvalidDataException("Not a managed PE");
    MetadataReader reader = pe.GetMetadataReader();
    AssemblyDefinition assembly = reader.GetAssemblyDefinition();
    string name = reader.GetString(assembly.Name);
    string? tfm = null;
    foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
    {
        var attribute = reader.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
        var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        if (reader.GetString(type.Name) != "TargetFrameworkAttribute") continue;
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() == 1) tfm = blob.ReadSerializedString();
    }
    if (mod && (name != "AutonomousColonists" || tfm != ".NETCoreApp,Version=v10.0"))
        throw new InvalidDataException("Unexpected mod identity or target framework");
    var references = reader.AssemblyReferences.Select(handle => {
        var reference = reader.GetAssemblyReference(handle);
        return new { name = reader.GetString(reference.Name), version = reference.Version.ToString() };
    }).ToArray();
    if (mod && references.Any(r => r.name.Contains("Harmony", StringComparison.OrdinalIgnoreCase) || r.name.StartsWith("LordKuper", StringComparison.Ordinal)))
        throw new InvalidDataException("Unexpected third party mod dependency");
    return new { name, version = assembly.Version.ToString(), targetFramework = tfm,
        machine = pe.PEHeaders.CoffHeader.Machine.ToString(), flags = pe.PEHeaders.CorHeader.Flags.ToString(),
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), references };
}
var report = new { privateReferences = new[] { "Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll" }
    .Select(name => Inspect(Path.Combine(args[0], name))).ToArray(), mod = Inspect(args[1], true),
    method = "Static PE metadata only. Compiler resolves API signatures. No game code executed.",
    androidExecuted = false };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Managed PE identity and dependencies verified; no proprietary binaries copied.");
