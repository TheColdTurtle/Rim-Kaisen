using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Black Flash (Section 22): when a player sorcerer lands a melee hit there is
    // a small chance to "spark" a Black Flash - bonus cursed damage, an instant
    // refill of cursed energy, and a temporary output surge.
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), nameof(Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget))]
    public static class Patch_BlackFlash
    {
        private const float BaseChance = 0.06f;

        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target,
            DamageWorker.DamageResult __result)
        {
            Pawn caster = __instance.CasterPawn;
            if (caster?.Faction == null || !caster.Faction.IsPlayer) return;
            if (!JJK_GradeUtility.IsSorcerer(caster)) return;
            if (!(target.Thing is Pawn victim) || victim == caster || victim.Dead) return;
            if (__result == null || __result.totalDamageDealt <= 0f) return;

            // Slightly more likely at low mood ("born of negative emotion").
            float chance = BaseChance;
            if (caster.needs?.mood != null && caster.needs.mood.CurLevelPercentage < 0.35f) chance += 0.04f;
            chance = Mathf.Clamp01(chance * (RimKaisenMod.Settings?.blackFlashChanceFactor ?? 1f));
            if (!Rand.Chance(chance)) return;

            BlackFlash.Trigger(caster, victim, __result.totalDamageDealt);
        }
    }

    public static class BlackFlash
    {
        public static void Trigger(Pawn caster, Pawn victim, float baseDamage)
        {
            GameComponent_CampaignHistory.Instance?.Add(caster, "JJK_RecordFlash", victim.LabelCap);
            // Bonus cursed-energy damage on the struck target.
            float extra = Mathf.Min(40f, baseDamage * 1.5f);
            victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, extra, 0.25f, -1f, caster));

            // Refill cursed energy to maximum.
            CompAbilities comp = caster.GetComp<CompAbilities>();
            AbilityResource res = comp?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res != null) res.energy = res.MaxEnergy;

            // Output surge buff.
            if (caster.health != null
                && caster.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_BlackFlashSurge) == null)
            {
                caster.health.AddHediff(JJK_DefOf.JJK_BlackFlashSurge);
            }

            // Feedback + a bit of grade XP.
            if (caster.Spawned && caster.Map != null)
            {
                MoteMaker.ThrowText(caster.DrawPos, caster.Map, "JJK_BlackFlash".Translate(), Color.red, 3.5f);
            }
            if (victim.Spawned && victim.Map != null)
            {
                FleckMaker.Static(victim.Position, victim.Map, FleckDefOf.ExplosionFlash, 2f);
            }
            GameComponent_JJK.Instance?.AddGradeXp(caster, 10);
        }
    }
}
