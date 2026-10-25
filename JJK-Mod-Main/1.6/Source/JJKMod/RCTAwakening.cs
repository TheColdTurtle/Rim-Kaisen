using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    public static class RCTAwakeningUtil
    {
        public static bool HasRCT(Pawn pawn) => pawn?.story?.traits != null
            && (pawn.story.traits.HasTrait(JJK_DefOf.JJK_RCT)
                || pawn.story.traits.HasTrait(JJK_DefOf.JJK_SoulMender));

        public static bool CanAwaken(Pawn pawn) => pawn != null && !pawn.Dead && !pawn.Destroyed
            && pawn.RaceProps.Humanlike && pawn.Faction == Faction.OfPlayer
            && pawn.story?.traits != null && JJK_GradeUtility.IsSorcerer(pawn)
            && !HasRCT(pawn);

        public static bool IsQualifyingDowning(Pawn pawn, DamageInfo? damage) => CanAwaken(pawn)
            && pawn.Downed && pawn.health.summaryHealth.SummaryHealthPercent <= 0.4f
            && damage.HasValue && damage.Value.Amount > 0f
            && damage.Value.Instigator is Pawn attacker && attacker != pawn && attacker.HostileTo(pawn);

        public static float Chance(bool researchFinished) => UnityEngine.Mathf.Clamp01(
            (researchFinished ? 0.10f : 0.05f) * (RimKaisenMod.Settings?.rctAwakeningFactor ?? 1f));

        public static bool TryMark(Pawn pawn, DamageInfo? damage)
        {
            if (!IsQualifyingDowning(pawn, damage)) return false;
            HediffDef marker = DefDatabase<HediffDef>.GetNamed("JJK_NearDeathInsight");
            if (pawn.health.hediffSet.HasHediff(marker)) return false;
            pawn.health.AddHediff(marker);
            return true;
        }

        public static bool Awaken(Pawn pawn)
        {
            if (!CanAwaken(pawn)) return false;
            pawn.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_RCT, 0, true));
            if (!HasRCT(pawn)) return false;
            pawn.GetComp<CompAbilities>()?.RecheckAbilities();
            Find.LetterStack.ReceiveLetter("JJK_RCTAwakeningLabel".Translate(),
                "JJK_RCTAwakeningText".Translate(pawn.LabelShortCap), LetterDefOf.PositiveEvent, pawn);
            return true;
        }
    }

    public class HediffCompProperties_RCTAwakening : HediffCompProperties
    {
        public HediffCompProperties_RCTAwakening() { compClass = typeof(HediffComp_RCTAwakening); }
    }

    public class HediffComp_RCTAwakening : HediffComp
    {
        private bool resolved;
        // Draw once when the combat event happens, not when a saved game is reloaded.
        private float roll;
        private float chance;
        public bool Resolved => resolved;

        public override void CompPostMake()
        {
            base.CompPostMake();
            roll = Rand.Value;
            chance = RCTAwakeningUtil.Chance(DefDatabase<ResearchProjectDef>
                .GetNamed("JJK_ReverseCursedTechnique").IsFinished);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref resolved, "resolved");
            Scribe_Values.Look(ref roll, "awakeningRoll", 1f);
            Scribe_Values.Look(ref chance, "awakeningChance", 0.05f);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!resolved && Pawn.IsHashIntervalTick(60)) ResolveRecovery();
        }

        public void ResolveRecovery()
        {
            if (resolved) return;
            // Dying ends this opportunity; resurrection must not award an awakening.
            if (Pawn.Dead || !RCTAwakeningUtil.CanAwaken(Pawn)) { resolved = true; return; }
            if (Pawn.Downed) return;
            resolved = true;
            if (roll < chance) RCTAwakeningUtil.Awaken(Pawn);
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            resolved = true;
        }

        public override string CompTipStringExtra => resolved
            ? "JJK_RCTInsightResolved".Translate()
            : "JJK_RCTInsightPending".Translate((chance * 100f).ToString("0"));
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_RCTCombatAwakening
    {
        public static void Postfix(Pawn_HealthTracker __instance, DamageInfo? dinfo)
            => RCTAwakeningUtil.TryMark(__instance.pawn, dinfo);
    }
}
