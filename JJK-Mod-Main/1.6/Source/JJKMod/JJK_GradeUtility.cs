using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JJKMod
{
    // Sorcerer grade tiers (Section 6). Tier index drives the
    // JJK_SorcererGrade hediff severity (tier * 0.2).
    public enum SorcererGrade
    {
        Grade4 = 0,
        Grade3 = 1,
        Grade2 = 2,
        Grade1 = 3,
        Special = 4,
        Calamity = 5
    }

    public static class JJK_GradeUtility
    {
        // Cumulative grade XP required to reach each tier.
        // Calamity is intentionally unreachable by XP (quest-locked).
        public static readonly int[] CumulativeXpForTier = { 0, 100, 400, 1400, 4900, int.MaxValue };

        // Highest grade reachable purely by grinding XP.
        public const SorcererGrade MaxXpGrade = SorcererGrade.Special;

        public static readonly string[] GradeLabels =
        {
            "grade 4", "grade 3", "grade 2", "grade 1", "special grade", "calamity grade"
        };

        public static SorcererGrade GradeForXp(int xp)
        {
            SorcererGrade result = SorcererGrade.Grade4;
            for (int tier = 1; tier <= (int)MaxXpGrade; tier++)
            {
                if (xp >= CumulativeXpForTier[tier])
                    result = (SorcererGrade)tier;
                else
                    break;
            }
            return result;
        }

        public static float SeverityForGrade(SorcererGrade grade)
        {
            return (int)grade * 0.2f;
        }

        // XP remaining to the next grade, or -1 if already at the XP cap.
        public static int XpToNextGrade(int xp)
        {
            SorcererGrade current = GradeForXp(xp);
            if (current >= MaxXpGrade) return -1;
            return CumulativeXpForTier[(int)current + 1] - xp;
        }

        // True if the pawn has a cursed energy reserve (and thus can hold a grade).
        public static bool IsSorcerer(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;
            return pawn.GetStatValue(JJK_DefOf.JJK_MaxCursedEnergy) > 0f;
        }
    }
}
