using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Generic instant self-buff: applies the extension's hediff to the caster.
    // Duration comes from the hediff's own Disappears comp (unlike the toggle,
    // which holds its hediff until switched off). Used for Kirin's blessing etc.
    public class SelfHediffExtension : DefModExtension
    {
        public HediffDef hediff;
    }

    public class Ability_SelfBuff : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            HediffDef buff = def.GetModExtension<SelfHediffExtension>()?.hediff;
            if (buff == null || pawn?.health == null) return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(buff);
            if (existing != null)
            {
                var disappears = existing.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                    disappears.ticksToDisappear = disappears.Props.disappearsAfterTicks.max;
            }
            else
            {
                pawn.health.AddHediff(buff);
            }
        }
    }

    // ===================================================================== //
    // Deadly Sentencing (Higuruma): judicial sorcery. Judgement passes a
    // verdict on a target - Forfeiture (their weapon is confiscated) if they
    // are armed, otherwise the Death Penalty (a mark that Executioner's Sword
    // can carry out with a single overwhelming stroke).
    // ===================================================================== //

    public class Ability_Judgement : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target.Dead) return;

            // Forfeiture: an armed defendant has their weapon confiscated.
            ThingWithComps weapon = target.equipment?.Primary;
            if (weapon != null)
            {
                target.equipment.TryDropEquipment(weapon, out _, target.Position);
                if (target.health != null && JJK_DefOf.JJK_Convicted != null
                    && target.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_Convicted) == null)
                {
                    target.health.AddHediff(JJK_DefOf.JJK_Convicted);
                }
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_VerdictForfeiture".Translate());
                return;
            }

            // Death Penalty: the unarmed (or already-convicted) defendant is condemned.
            if (target.health != null && JJK_DefOf.JJK_DeathPenalty != null
                && target.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_DeathPenalty) == null)
            {
                target.health.AddHediff(JJK_DefOf.JJK_DeathPenalty);
            }
            MoteMaker.ThrowText(target.DrawPos, map, "JJK_VerdictDeathPenalty".Translate());
        }
    }

    // Executioner's Sword: carry out the sentence. Devastating against a target
    // under the Death Penalty; refuses to strike anyone not condemned.
    public class Ability_ExecutionersSword : Ability
    {
        private const float Damage = 120f;
        private const float Chunk = 20f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target.Dead) return;

            Hediff sentence = JJK_DefOf.JJK_DeathPenalty == null ? null
                : target.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_DeathPenalty);
            if (sentence == null)
            {
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_NoVerdict".Translate());
                return;
            }

            float total = Damage * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / Chunk));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                var dinfo = new DamageInfo(DamageDefOf.Cut, Chunk, 0f, -1f, pawn);
                dinfo.SetIgnoreArmor(true);
                target.TakeDamage(dinfo);
            }
            // The sentence is served either way.
            if (!target.Dead) target.health.RemoveHediff(sentence);
        }
    }
}
