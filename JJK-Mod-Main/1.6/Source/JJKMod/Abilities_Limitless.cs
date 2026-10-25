using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Infinity (requires RCT; Six Eyes makes it sustainable): a toggleable passive
    // that, while active, brings incoming attacks to a halt (Patch_Infinity). It
    // bleeds cursed energy heavily every tick - a cost that is crippling for a
    // normal sorcerer but trivial for a Six Eyes bearer, whose 99% efficiency
    // shrinks the drain to almost nothing. Drops if the pawn runs dry or is sealed.
    public class Ability_Infinity : Ability_Toggleable
    {
        // Base cursed energy bled per tick while active, BEFORE efficiency. At full
        // cost this empties a large reserve in seconds; at Six Eyes efficiency (1%)
        // it is a slow trickle.
        private const float DrainPerTick = 0.6f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            if (pawn?.health != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_Infinity) == null)
            {
                pawn.health.AddHediff(JJK_DefOf.JJK_Infinity);
            }
        }

        public override void End()
        {
            base.End();
            Hediff h = pawn?.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_Infinity);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        public override void Tick()
        {
            base.Tick();
            if (!Active || pawn?.health == null) return;

            // Sealed technique collapses Infinity.
            if (TechniqueSealUtil.IsSealed(pawn) || DomainAmplificationUtil.Active(pawn))
            {
                End();
                return;
            }

            AbilityResource res = pawn.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res == null) return;

            float efficiency = Mathf.Max(0.01f, pawn.GetStatValue(JJK_DefOf.JJK_CursedEnergyUsageMultiplier));
            res.energy -= DrainPerTick * efficiency;
            if (res.energy <= 0f)
            {
                res.energy = 0f;
                End();
            }
        }
    }

    // Cursed Technique Lapse: Blue. A 3-tile pull that drags enemies to the target
    // point and batters them. The one Limitless move castable without the Six Eyes
    // or Reverse Cursed Technique.
    public class Ability_LapseBlue : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            float o = Output(pawn);
            JJK_LimitlessUtil.PullToward(pawn, curTarget.Cell, 3.5f, 3, 25f * o, DamageDefOf.Blunt);
        }

        public static float Output(Pawn p) => Mathf.Max(0.5f, p.GetStatValue(JJK_DefOf.JJK_CTOutput));
    }

    // Cursed Technique Reversal: Red. A violent burst of positive cursed energy -
    // a true explosion (screen shake and all) that flings survivors away.
    public class Ability_ReversalRed : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            float o = Ability_LapseBlue.Output(pawn);
            IntVec3 c = curTarget.Cell;
            Map map = pawn?.Map;
            if (map == null) return;

            GenExplosion.DoExplosion(c, map, 5.9f, DamageDefOf.Bomb, pawn, Mathf.RoundToInt(40f * o));
            // Hurl any survivors away from the caster.
            JJK_LimitlessUtil.BlastAway(pawn, c, 6f, 3, 0f, DamageDefOf.Blunt);
        }
    }

    // Hollow Technique: Purple. A long linear beam of imaginary mass that tears
    // through everything between the caster and far beyond the target.
    public class Ability_HollowPurple : Ability
    {
        private const int BeamLength = 40;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            float o = Ability_LapseBlue.Output(pawn);
            JJK_LimitlessUtil.LineBeam(pawn, curTarget.Cell, BeamLength, 65f * o, DamageDefOf.Bomb);
        }
    }

    // Limitless teleport: instantly relocate the caster to the target cell.
    public class Ability_Teleport : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            IntVec3 c = curTarget.Cell;
            Map map = pawn?.Map;
            if (map == null || !c.InBounds(map) || !c.Standable(map)) return;

            IntVec3 from = pawn.Position;
            if (pawn.Spawned)
            {
                FleckMaker.Static(from, map, FleckDefOf.PsycastSkipFlashEntry, 1.2f);
            }
            pawn.Position = c;
            pawn.Notify_Teleported(false, true);
            FleckMaker.Static(c, map, FleckDefOf.PsycastSkipInnerExit, 1.2f);
        }
    }
}
