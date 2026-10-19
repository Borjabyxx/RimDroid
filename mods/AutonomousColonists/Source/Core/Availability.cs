namespace AutonomousColonists.Core;

internal static class Availability
{
    public static bool Paused(bool incapacitated, float mood, float rest, bool previouslyPaused) =>
        incapacitated || mood < (previouslyPaused ? 0.30f : 0.20f) || rest < (previouslyPaused ? 0.25f : 0.15f);
}
