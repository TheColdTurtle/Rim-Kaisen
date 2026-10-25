using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Use effect for consumable talismans: instantly restores cursed energy to
    // the user. Fails gracefully (message, item not consumed is handled by the
    // CanBeUsedBy gate) for non-sorcerers.
    public class CompProperties_RestoreCursedEnergy : CompProperties_UseEffect
    {
        // Cursed energy restored on use.
        public float amount = 100f;

        public CompProperties_RestoreCursedEnergy()
        {
            compClass = typeof(CompUseEffect_RestoreCursedEnergy);
        }
    }

    public class CompUseEffect_RestoreCursedEnergy : CompUseEffect
    {
        public new CompProperties_RestoreCursedEnergy Props => (CompProperties_RestoreCursedEnergy)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            AbilityResource res = usedBy?.GetComp<CompAbilities>()
                ?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res == null) return;

            res.energy = Mathf.Min(res.MaxEnergy, res.energy + Props.amount);
            if (usedBy.Spawned)
            {
                FleckMaker.ThrowMetaIcon(usedBy.Position, usedBy.Map, FleckDefOf.Meditating);
                MoteMaker.ThrowText(usedBy.DrawPos, usedBy.Map,
                    "JJK_TalismanRestored".Translate(Mathf.RoundToInt(Props.amount)));
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy) == null)
                return "JJK_CannotReadNotSorcerer".Translate();
            return base.CanBeUsedBy(p);
        }
    }
}
