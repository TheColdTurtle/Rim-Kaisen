using System;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // ===================================================================== //
    //   Boogie Woogie (Todo)   - swap places with a target pawn. Needs hands.
    //   Ratio Technique (Nanami) - melee Ratio strike + delayed AoE Collapse.
    // ===================================================================== //

    // Boogie Woogie: a clap of cursed energy instantly swaps the caster and the
    // target pawn (friend or foe). Clapping requires two working hands, so a pawn
    // missing a hand cannot cast it.
    public class Ability_BoogieWoogie : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            if (CursedTechUtil.WorkingHandCount(pawn) < 2
                || (pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f) <= 0f) return;
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target == pawn) return;
            if (!pawn.Spawned || !target.Spawned || target.Map != map) return;
            if (MahoragaPhenomena.ControlFactor(target) <= 0f) return;

            IntVec3 here = pawn.Position;
            IntVec3 there = target.Position;

            // Clean swap - no visual effects.
            pawn.Position = there;
            pawn.Notify_Teleported(false, true);
            target.Position = here;
            target.Notify_Teleported(false, true);

            // Cooldown scales inversely with the caster's Manipulation: nimbler hands
            // (e.g. bionic arms) clap again sooner; impaired hands take longer.
            ApplyManipulationCooldown();
        }

        private void ApplyManipulationCooldown()
        {
            if (def?.abilityTiers == null || def.abilityTiers.Count == 0) return;
            int baseCd = def.abilityTiers[0].cooldownTicks;
            float globalMult = pawn?.GetStatValue(JJK_DefOf.JJK_CTCooldownMultiplier) ?? 1f;
            SetCooldown(Mathf.Max(1, Mathf.RoundToInt(baseCd * globalMult * CooldownFactor())));
        }

        // Clapping needs two working hands.
        public override Func<string> CanBeActivatedValidator()
        {
            return delegate
            {
                if (CursedTechUtil.WorkingHandCount(pawn) < 2
                    || (pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f) <= 0f)
                    return "JJK_BoogieNeedsHands".Translate();
                return "";
            };
        }

        private float CooldownFactor()
        {
            float manip = pawn?.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 1f;
            return Mathf.Clamp(1f / Mathf.Max(0.1f, manip), 0.35f, 2.5f);
        }
    }

    // Ratio Technique: divide the target 7:3 to force a weak point, then strike it
    // at melee range. Instant, armor-cleaving single hit.
    public class Ability_RatioStrike : Ability
    {
        private const float BaseDamage = 45f;
        private const float Chunk = 15f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead) return;

            float total = BaseDamage * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / Chunk));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                // High armor penetration: the forced weak point cleaves through armor.
                target.TakeDamage(new DamageInfo(DamageDefOf.Cut, Chunk, 2f, -1f, pawn));
            }

            if (target.Spawned)
                FleckMaker.ThrowMicroSparks(target.DrawPos, target.Map);
        }
    }

    // Collapse: slam the forced weak point into a delayed area blast. The explosion
    // lands after a short fuse, giving the caster time to retreat before it goes off.
    public class Ability_Collapse : Ability
    {
        private const int FuseTicks = 240;   // ~4 seconds to clear the blast zone
        private const float Radius = 4.5f;
        private const int BaseDamage = 60;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Map map = pawn?.Map;
            if (map == null) return;

            IntVec3 center = curTarget.Cell;
            int dmg = Mathf.RoundToInt(BaseDamage * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput)));
            MapComponent_DelayedBlasts.Queue(map, center, FuseTicks, Radius,
                DamageDefOf.Bomb, dmg, pawn);

            if (center.InBounds(map))
                FleckMaker.Static(center, map, FleckDefOf.ExplosionFlash, 2f);
            MoteMaker.ThrowText(center.ToVector3Shifted(), map, "JJK_CollapseArmed".Translate(), 3f);
        }
    }
}
