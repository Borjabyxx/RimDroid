// The attribute that AccessChecks.java (app) writes into mod assemblies on RimWorld 1.7:
// [assembly: IgnoresAccessChecksTo("Assembly-CSharp")] and the like. Stock .NET honours it by its name
// alone but defines no such type; code that reads an assembly's attributes (Harmony's mod reads its
// version that way) needs the type to exist, so the mods' reference points here.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}
