using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public static class PositiveEnergyUtil
    {
        // Restrict the lore effect to actual marked curses, not every Anomaly entity.
        public static bool IsCurse(Pawn target) => target?.def?.HasModExtension<CursedSpiritExtension>() == true;

        public static bool CanExorcise(Pawn caster, Pawn target) => caster?.Spawned == true
            && target?.Spawned == true && !target.Dead && target != caster && target.Map == caster.Map
            && IsCurse(target) && target.HostileTo(caster)
            && caster.Position.InHorDistOf(target.Position, 1.9f)
            && GenSight.LineOfSight(caster.Position, target.Position, caster.Map);

        public static void Strike(Pawn caster, Pawn target, float damage)
        {
            for (float remaining = damage; remaining > 0f && !target.Dead && !target.Destroyed; remaining -= 12f)
            {
                var hit = new DamageInfo(DamageDefOf.Burn, Mathf.Min(12f, remaining), 0f, -1f, caster);
                hit.SetIgnoreArmor(true);
                target.TakeDamage(hit);
            }
        }
    }

    public class Ability_PositiveEnergyOutput : Ability
    {
        public override bool CanHitTarget(LocalTargetInfo target) =>
            PositiveEnergyUtil.CanExorcise(pawn, target.Pawn) && base.CanHitTarget(target);

        public override void Start(bool consumeEnergy = true)
        {
            Pawn target = curTarget.Pawn;
            if (pawn?.story?.traits?.HasTrait(JJK_DefOf.JJK_SoulMender) != true
                || !CanHitTarget(curTarget)) return;
            base.Start(consumeEnergy);
            PositiveEnergyUtil.Strike(pawn, target, 60f * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput)));
        }
    }
}
