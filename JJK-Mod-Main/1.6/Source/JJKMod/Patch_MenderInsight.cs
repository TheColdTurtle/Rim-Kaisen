using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // The soul mender's gift can be BORN (the rare JJK_SoulMender trait rolled at
    // pawn generation) or EARNED: an ordinary Reverse Cursed Technique user who
    // spends their life tending other people's wounds eventually learns enough
    // about a body that is not their own to reverse cursed energy into it.
    //
    // Progress accrues per tend (GameComponent_JJK.menderInsight) and only for a
    // doctor skilled enough to learn anything from the work. At the threshold the
    // pawn's JJK_RCT trait is replaced by JJK_SoulMender - a strict upgrade, since
    // the mender trait grants the same RCT tree plus the outward "mend" ability.
    public static class MenderInsightUtil
    {
        // Insight needed to awaken.
        public const int AwakenThreshold = 600;

        // Below this Medicine skill the doctor is too green to learn from the work.
        public const int MinMedicineSkill = 8;

        // Called after a successful tend. Returns true if this tend awakened them.
        public static bool NotifyTended(Pawn doctor, Pawn patient)
        {
            if (doctor == null || patient == null || doctor == patient) return false;
            if (doctor.story?.traits == null) return false;
            // Only a colony pawn's practice is tracked, so the save does not
            // accumulate progress for every wandering trader's medic.
            if (doctor.Faction == null || !doctor.Faction.IsPlayer) return false;

            // Must already be an RCT sorcerer, and not already a mender.
            if (JJK_DefOf.JJK_RCT == null || !doctor.story.traits.HasTrait(JJK_DefOf.JJK_RCT)) return false;
            if (JJK_DefOf.JJK_SoulMender != null && doctor.story.traits.HasTrait(JJK_DefOf.JJK_SoulMender)) return false;
            if (!JJK_GradeUtility.IsSorcerer(doctor)) return false;

            int medicine = doctor.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            if (medicine < MinMedicineSkill) return false;

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return false;

            // A better doctor learns more from the same wound.
            gc.AddMenderInsight(doctor, medicine);
            if (gc.GetMenderInsight(doctor) < AwakenThreshold) return false;

            Awaken(doctor);
            return true;
        }

        // Replace the plain RCT trait with the soul mender trait and let TMF
        // re-scan, so the outward mend ability appears immediately.
        public static void Awaken(Pawn pawn)
        {
            if (pawn?.story?.traits == null || JJK_DefOf.JJK_SoulMender == null) return;
            if (pawn.story.traits.HasTrait(JJK_DefOf.JJK_SoulMender)) return;

            // Drop the old trait first so the two never coexist (they are declared
            // conflicting); the mender trait re-grants the same RCT tree, and the
            // RecheckAbilities below restores the gizmos in the same call.
            if (JJK_DefOf.JJK_RCT != null)
            {
                Trait old = pawn.story.traits.GetTrait(JJK_DefOf.JJK_RCT);
                if (old != null) pawn.story.traits.RemoveTrait(old);
            }
            pawn.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_SoulMender, 0, true));

            // Adding a trait at runtime does not make TMF re-scan ability sources
            // on its own (same reason Gene_CursedTechnique forces this).
            pawn.GetComp<CompAbilities>()?.RecheckAbilities();

            GameComponent_JJK.Instance?.ClearMenderInsight(pawn);

            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "JJK_SoulMenderAwakenedLabel".Translate(),
                    "JJK_SoulMenderAwakened".Translate(pawn.LabelShortCap),
                    LetterDefOf.PositiveEvent, pawn);
            }
        }
    }

    // Every completed tend is a chance to understand another body a little better.
    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    public static class Patch_TendUtility_MenderInsight
    {
        public static void Postfix(Pawn doctor, Pawn patient)
        {
            MenderInsightUtil.NotifyTended(doctor, patient);
        }
    }
}
