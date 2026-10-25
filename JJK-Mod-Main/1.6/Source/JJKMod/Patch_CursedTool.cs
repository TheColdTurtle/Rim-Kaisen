using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Cursed tools bite harder in a sorcerer's hands. After a melee hit with a
    // weapon that has a Comp_CursedTool, apply two on-hit bonuses:
    //   1. CE scaling - extra cursed-energy damage proportional to how far the
    //      wielder's CT Output exceeds the baseline (i.e. their sorcerer grade).
    //   2. Spirit bonus - extra damage when the target is a cursed spirit.
    // A non-sorcerer wielder (CT Output ~1) still gets the spirit bonus and the
    // tool's raw stats, but no CE-scaling term - matching the lore where cursed
    // tools work for anyone but are deadliest channelling a sorcerer's energy.
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), nameof(Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget))]
    public static class Patch_CursedTool
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target,
            DamageWorker.DamageResult __result)
        {
            Comp_CursedTool tool = __instance?.EquipmentSource?.GetComp<Comp_CursedTool>();
            if (tool == null) return;

            Pawn caster = __instance.CasterPawn;
            if (caster == null) return;
            if (!(target.Thing is Pawn victim) || victim == caster || victim.Dead) return;
            if (__result == null || __result.totalDamageDealt <= 0f) return;

            float dealt = __result.totalDamageDealt;

            // Inverted Spear of Heaven: nullify the struck pawn's cursed technique.
            if (tool.Props.sealsTechnique) ApplyTechniqueSeal(victim, tool.Props.sealTicks);

            // Black Rope: disrupt (degrade, not seal) the target's technique.
            if (tool.Props.disruptsTechnique) ApplyDisruption(victim, tool.Props.disruptTicks);

            // Nobara's hammer: each hit drives cursed nails into the target.
            if (tool.Props.nailsPerHit > 0) NailUtil.AddNails(victim, tool.Props.nailsPerHit);

            // Kamutoke: lightning discharge briefly stuns.
            if (tool.Props.stunOnHitTicks > 0)
            {
                using (new MahoragaExposure("JJK_Adapt_Lightning", caster))
                    victim.stances?.stunner?.StunFor(tool.Props.stunOnHitTicks, caster, false, true);
            }

            // Split Soul Katana: only a heavenly-restricted wielder cuts the soul,
            // dealing armor-ignoring bonus damage. Otherwise the tool is ordinary.
            if (tool.Props.heavenlyRestrictionSoulFactor > 0f && JJK_Utils.IsHeavenlyRestricted(caster))
            {
                float soul = dealt * tool.Props.heavenlyRestrictionSoulFactor;
                if (soul > 0.5f)
                {
                    // armorPenetration 1.0 -> bypasses all armor (soul damage).
                    using (new MahoragaExposure("JJK_Adapt_SoulCut", caster))
                        victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, soul, 1f, -1f, caster));
                }
            }

            float bonus = 0f;

            // 1. Cursed-energy amplification, scaled by the wielder's CT Output.
            float output = caster.GetStatValue(JJK_DefOf.JJK_CTOutput);
            if (output > 1f && tool.Props.ceOutputScaling > 0f)
            {
                bonus += dealt * (output - 1f) * tool.Props.ceOutputScaling;
            }

            // 2. Anti-spirit edge.
            if (tool.Props.spiritBonusFactor > 0f && JJK_Utils.IsCursedSpirit(victim))
            {
                bonus += dealt * tool.Props.spiritBonusFactor;
            }

            // 3. Nanami's blunt sword: strike the forced 7:3 weak point.
            if (tool.Props.ratioMarkBonusFactor > 0f && JJK_DefOf.JJK_RatioMark != null
                && victim.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_RatioMark) != null)
            {
                bonus += dealt * tool.Props.ratioMarkBonusFactor;
            }

            // 4. Kusakabe's katana: the technique-less swordsman's edge.
            if (tool.Props.noTechniqueBonusFactor > 0f && !JJK_Utils.HasCursedTechnique(caster))
            {
                bonus += dealt * tool.Props.noTechniqueBonusFactor;
            }

            // 5. Okkotsu's katana: raw reserves overload the blade.
            if (tool.Props.maxCEScalingFactor > 0f)
            {
                float maxCE = caster.GetStatValue(JJK_DefOf.JJK_MaxCursedEnergy);
                if (maxCE > 0f) bonus += dealt * (maxCE / 200f) * tool.Props.maxCEScalingFactor;
            }

            if (bonus > 0.5f)
            {
                // Tag the weapon so a kill by the bonus damage still credits the
                // tool's on-kill effects (Hiten's refund, Black Sword's frenzy).
                DamageInfo dinfo = new DamageInfo(DamageDefOf.Cut, bonus, 0.5f, -1f, caster,
                    weapon: __instance.EquipmentSource.def);
                victim.TakeDamage(dinfo);

                if (victim.Spawned && victim.Map != null)
                {
                    FleckMaker.Static(victim.Position, victim.Map, FleckDefOf.PsycastAreaEffect, 0.6f);
                }
            }

            // Slaughter Demon: the hit shoves the target back a step.
            if (tool.Props.knockbackCells > 0 && victim.Spawned && !victim.Dead)
            {
                KnockBack(caster, victim, tool.Props.knockbackCells);
            }
        }

        // Shove the victim directly away from the attacker, stopping at the first
        // unstandable cell. A pure reposition - no damage, no stun.
        private static void KnockBack(Pawn caster, Pawn victim, int cells)
        {
            Map map = victim.Map;
            if (map == null) return;
            IntVec3 delta = victim.Position - caster.Position;
            int sx = delta.x > 0 ? 1 : (delta.x < 0 ? -1 : 0);
            int sz = delta.z > 0 ? 1 : (delta.z < 0 ? -1 : 0);
            if (sx == 0 && sz == 0) return;

            IntVec3 dest = victim.Position;
            for (int i = 0; i < cells; i++)
            {
                IntVec3 next = new IntVec3(dest.x + sx, 0, dest.z + sz);
                if (!next.InBounds(map) || !next.Standable(map)) break;
                dest = next;
            }
            if (dest == victim.Position) return;
            victim.Position = dest;
            victim.Notify_Teleported(true, true);
        }

        // Apply (or refresh) the Black Rope's technique disruption on the victim.
        private static void ApplyDisruption(Pawn victim, int ticks)
        {
            if (victim?.health == null || ticks <= 0 || JJK_DefOf.JJK_TechniqueDisrupted == null) return;
            Hediff h = victim.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_TechniqueDisrupted);
            if (h == null) h = victim.health.AddHediff(JJK_DefOf.JJK_TechniqueDisrupted);
            HediffComp_Disappears dis = h.TryGetComp<HediffComp_Disappears>();
            if (dis != null) dis.ticksToDisappear = Mathf.Max(dis.ticksToDisappear, ticks);
        }

        // Apply (or refresh) the technique seal on the victim.
        private static void ApplyTechniqueSeal(Pawn victim, int ticks)
        {
            if (victim?.health == null || ticks <= 0) return;
            Hediff h = victim.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_TechniqueSealed);
            if (h == null) h = victim.health.AddHediff(JJK_DefOf.JJK_TechniqueSealed);
            HediffComp_Disappears dis = h.TryGetComp<HediffComp_Disappears>();
            if (dis != null) dis.ticksToDisappear = Mathf.Max(dis.ticksToDisappear, ticks);

            if (victim.Spawned && victim.Map != null)
            {
                FleckMaker.Static(victim.Position, victim.Map, FleckDefOf.PsycastSkipFlashEntry, 0.8f);
            }
        }
    }
}
