using RimWorld;
using Verse;

namespace JJKMod
{
    // Use effect for technique remnants: studying the remnant implants the given
    // cursed-technique gene as a xenogene. If the user has no cursed energy
    // reserve at all, a modest reserve gene is granted too, so the technique is
    // actually castable. This is the in-game path for a colonist to LEARN a
    // cursed technique - remnants drop from exorcised boss curses.
    public class CompProperties_ImplantGene : CompProperties_UseEffect
    {
        public GeneDef gene;

        public CompProperties_ImplantGene()
        {
            compClass = typeof(CompUseEffect_ImplantGene);
        }
    }

    public class CompUseEffect_ImplantGene : CompUseEffect
    {
        public new CompProperties_ImplantGene Props => (CompProperties_ImplantGene)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            if (usedBy?.genes == null || Props.gene == null) return;

            usedBy.genes.AddGene(Props.gene, true);

            // A technique without a reserve is uncastable; grant a modest reserve
            // if the pawn has none (heavenly-restricted pawns are excluded by
            // CanBeUsedBy, so this only fires for plain non-sorcerers).
            if (!JJK_Utils.HasReserveGene(usedBy) && JJK_DefOf.JJK_AverageReserves != null)
            {
                usedBy.genes.AddGene(JJK_DefOf.JJK_AverageReserves, true);
            }

            Messages.Message("JJK_RemnantLearned".Translate(usedBy.LabelShortCap, Props.gene.LabelCap),
                usedBy, MessageTypeDefOf.PositiveEvent, false);
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p?.genes == null || !(p.RaceProps?.Humanlike ?? false))
                return "JJK_RemnantNotHuman".Translate();
            if (JJK_Utils.IsHeavenlyRestricted(p))
                return "JJK_RemnantRestricted".Translate();
            if (Props.gene != null && p.genes.HasActiveGene(Props.gene))
                return "JJK_RemnantAlreadyKnown".Translate();
            return base.CanBeUsedBy(p);
        }
    }
}
