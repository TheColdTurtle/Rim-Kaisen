using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Generic instant single-target strike, tuned from XML. Used for sword-art
    // moves (Evening Moon Sword Drawing etc.) that hit hard at close range with
    // no projectile. Damage scales with CT output; optionally also with the
    // caster's Melee skill (a swordsman's art, not a technique's power).
    public class StrikeAbilityExtension : DefModExtension
    {
        public float damage = 30f;
        public float armorPen = 0.4f;
        public DamageDef damageDef;
        public bool scaleWithMeleeSkill = false;
    }

    public class Ability_StrikeTarget : Ability
    {
        private const float Chunk = 13f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead) return;

            StrikeAbilityExtension ext = def.GetModExtension<StrikeAbilityExtension>();
            float damage = ext?.damage ?? 30f;
            float armorPen = ext?.armorPen ?? 0.4f;
            DamageDef damageDef = ext?.damageDef ?? DamageDefOf.Cut;

            float factor = Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            if (ext != null && ext.scaleWithMeleeSkill)
            {
                // 0.7x at unskilled up to ~1.7x at legendary melee.
                int melee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                factor *= 0.7f + melee * 0.05f;
            }

            float total = damage * factor;
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / Chunk));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                target.TakeDamage(new DamageInfo(damageDef, Chunk, armorPen, -1f, pawn));
            }
            if (target.Spawned) FleckMaker.ThrowMicroSparks(target.DrawPos, target.Map);
        }
    }
}
