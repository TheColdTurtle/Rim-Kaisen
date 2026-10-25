using RimWorld;
using UnityEngine;
using TaranMagicFramework;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // ===================================================================== //
    // The two anti-domain answers beyond Simple Domain (design doc: the
    // counters every sorcerer without a domain of their own has to learn).
    // Both are scroll-taught and grade-free, like Simple Domain, because they
    // are technique-agnostic craft rather than innate talent.
    //
    // The three counters are deliberately NOT interchangeable:
    //   Simple Domain          - area, protects allies too, ground down over time.
    //   Domain Amplification   - self only, no degradation, heavy upkeep, and it
    //                            is the one that reaches through Infinity.
    //   Falling Blossom Emotion- self only, one perfect parry, then gone. Cheap
    //                            and instant, so it is the panic button.
    // ===================================================================== //

    // Domain Amplification. Wraps the caster in a thin layer of their own domain,
    // nullifying cursed techniques on contact: hostile domain sure-hit slides off
    // (MapComponent_DomainManager.IsShieldedFromSureHit) and the caster's own blows
    // reach through a Limitless user's Infinity (InfinityUtil.CanPierceInfinity).
    // Holding it open bleeds cursed energy, so it is a window you open to close a
    // fight, not a stance you live in.
    public class Ability_DomainAmplification : Ability_Toggleable
    {
        // Per-tick cursed energy bled while active, BEFORE efficiency. Lighter than
        // Infinity's 0.6 - Amplification only sheathes the body, it does not hold an
        // infinite gap open - but still steep enough that a normal sorcerer can only
        // sustain it for a push, not a whole raid.
        private const float DrainPerTick = 0.35f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            if (pawn?.health != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_DomainAmplification) == null)
            {
                pawn.health.AddHediff(JJK_DefOf.JJK_DomainAmplification);
            }
            DomainAmplificationUtil.EndInnateToggles(pawn);
        }

        public override void End()
        {
            base.End();
            Hediff h = pawn?.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_DomainAmplification);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        public override void Tick()
        {
            base.Tick();
            if (!Active || pawn?.health == null) return;

            // A sealed technique cannot maintain a domain layer, same as Infinity.
            if (TechniqueSealUtil.IsSealed(pawn))
            {
                End();
                return;
            }

            AbilityResource res = pawn.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res == null) { End(); return; }
            if (pawn.IsHashIntervalTick(30)) DomainAmplificationUtil.EndInnateToggles(pawn);

            float efficiency = Mathf.Max(0.01f, pawn.GetStatValue(JJK_DefOf.JJK_CursedEnergyUsageMultiplier));
            res.energy -= DrainPerTick * efficiency;
            if (res.energy <= 0f)
            {
                res.energy = 0f;
                End();
            }
        }
    }
}
