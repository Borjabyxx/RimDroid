// libslang.so stand-in for RimWorld 1.7 on Android. The game compiles shaders at runtime with Slang
// (closed binary for desktop only), but first looks them up in the prebuilt SPIR-V pack inside
// Assets.zip. The pack key includes Slang's build tag (RuntimeShaderCompiler hashes
// "<Waylith version>...<spGetBuildTagString()>"), so without a libslang the game could not even
// find its prebuilt shaders. This stub answers the build tag of the Slang the game ships with
// (2026.19, read from the Linux libslang.so) and nothing else: a shader missing from the pack then
// fails to compile with EntryPointNotFoundException, which the engine catches.
#define API __attribute__((visibility("default")))

API const char* spGetBuildTagString(void) { return "2026.19"; }
