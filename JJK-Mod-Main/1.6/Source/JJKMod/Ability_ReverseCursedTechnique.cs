using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Shared healing routine for every "knit the body back together" technique:
    // Reverse Cursed Technique (self and outward) and Idle Transfiguration's Body
    // Reconstruction. Spends a pool of healing across the target's injuries,
    // worst first - bleeding wounds before merely deep ones.
    public static class RCTHealUtil
    {
        public static bool HealInjuries(Pawn target, float pool)
        {
            if (target?.health == null || pool <= 0f) return false;

            List<Hediff_Injury> injuries = target.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0f && (h.def.tendable || h.Bleeding))
                .OrderByDescending(h => h.Bleeding)
                .ThenByDescending(h => h.Severity)
                .ToList();

            bool healedAny = false;
            foreach (Hediff_Injury injury in injuries)
            {
                if (pool <= 0f) break;
                float amount = Mathf.Min(pool, injury.Severity);
                injury.Heal(amount);
                pool -= amount;
                healedAny = true;
            }
            return healedAny;
        }

        public static void ThrowHealText(Pawn at, string text)
        {
            if (at == null || !at.Spawned || at.Map == null) return;
            MoteMaker.ThrowText(at.DrawPos, at.Map, text, new Color(0.7f, 0.9f, 1f), 3f);
        }
    }

    // Reverse Cursed Technique: spend cursed energy to heal the caster's own
    // injuries. The size of the heal scales with the caster's CT Output (grade).
    // Gated by the RCT ability tree, which only a trait can unlock (JJK_RCT or
    // the rarer JJK_SoulMender).
    public class Ability_ReverseCursedTechnique : Ability
    {
        // Healing pool per cast, before the CT Output multiplier. At 50 energy a
        // cast, that is 0.5 healing per point of cursed energy spent - the
        // benchmark the outward version is deliberately measured against.
        private const float SelfHealPool = 25f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            if (pawn?.health == null) return;

            float pool = SelfHealPool * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            bool healed = RCTHealUtil.HealInjuries(pawn, pool);
            RCTHealUtil.ThrowHealText(pawn,
                (healed ? "JJK_RCTHealed" : "JJK_RCTNothingToHeal").Translate());
        }
    }

    // Reverse Cursed Technique turned OUTWARD - the rare gift of a soul mender.
    // Reversing your own cursed energy inside someone else's body is far harder
    // than mending yourself: a smaller pool for a much larger cost (20 healing
    // for 80 energy = 0.25 per point, exactly half the efficiency of the self
    // heal), on a longer cooldown, and only at a touch.
    public class Ability_ReverseCursedTechniqueOther : Ability
    {
        private const float OtherHealPool = 20f;

        public override void Start(bool consumeEnergy = true)
        {
            Pawn target = curTarget.Pawn;
            if (target == null || target == pawn || target.Dead || target.health == null) return;
            // Positive energy cannot heal bodies made of cursed energy.
            if (PositiveEnergyUtil.IsCurse(target)) return;
            base.Start(consumeEnergy);

            float pool = OtherHealPool * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            bool healed = RCTHealUtil.HealInjuries(target, pool);
            RCTHealUtil.ThrowHealText(target, healed
                ? "JJK_RCTHealedOther".Translate(target.LabelShortCap)
                : "JJK_RCTNothingToHeal".Translate());
        }

        public override bool CanHitTarget(LocalTargetInfo target) => target.Pawn != null
            && target.Pawn != pawn && !target.Pawn.Dead && !PositiveEnergyUtil.IsCurse(target.Pawn)
            && base.CanHitTarget(target);
    }
}
