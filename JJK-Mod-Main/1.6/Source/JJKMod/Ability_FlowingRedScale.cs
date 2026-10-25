using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // Flowing Red Scale (Blood Manipulation): a toggleable self-buff. While
    // active it applies the JJK_FlowingRedScale hediff (+melee damage, +move
    // speed) and reserves cursed energy via the tier's maxEnergyOffset.
    public class Ability_FlowingRedScale : Ability_Toggleable
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            ApplyBuff();
        }

        public override void End()
        {
            base.End();
            RemoveBuff();
        }

        private void ApplyBuff()
        {
            if (pawn?.health == null) return;
            if (pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_FlowingRedScale) == null)
            {
                pawn.health.AddHediff(JJK_DefOf.JJK_FlowingRedScale);
            }
        }

        private void RemoveBuff()
        {
            Hediff h = pawn?.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_FlowingRedScale);
            if (h != null) pawn.health.RemoveHediff(h);
        }
    }
}
