using RimWorld;
using Verse;

namespace AutonomousColonists;

internal static class WorkScoring
{
    public static float Aptitude(Pawn pawn, WorkTypeDef work)
    {
        if (work.relevantSkills == null || work.relevantSkills.Count == 0) return 1f;
        float total = 0f;
        int valid = 0;
        foreach (SkillDef definition in work.relevantSkills)
        {
            SkillRecord skill = pawn.skills.GetSkill(definition);
            if (skill == null || skill.TotallyDisabled) continue;
            total += skill.Level * 1.5f + (skill.passion == Passion.Major ? 10f : skill.passion == Passion.Minor ? 4f : 0f);
            valid++;
        }
        return valid == 0 ? 0f : total / valid;
    }
}
