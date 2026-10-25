using System.Collections.Generic;
using RimWorld;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // A cursed energy reserve gene. Beyond its statOffset on JJK_MaxCursedEnergy
    // (handled by vanilla Gene), it keeps the pawn's sorcerer systems in sync:
    //  - removing it strips the grade hediff (a pawn with no reserve cannot hold a
    //    sorcerer grade, Section 7.4);
    //  - adding/removing it re-checks TMF abilities so cursed-technique genes that
    //    were granted earlier (or are present from another source) re-register
    //    their trees. Without this, giving a pawn (notably a baseliner) a new
    //    reserve gene could leave its CT genes "dead" - the technique gizmos vanish;
    //  - only the single highest-tier reserve gene is kept; a stronger reserve
    //    overrides any weaker one rather than stacking with it.
    public class Gene_CursedEnergyReserve : Gene
    {
        public override void PostAdd()
        {
            base.PostAdd();
            EnforceHighestReserveOnly();
            GameComponent_JJK.Instance?.UpdateGradeHediff(pawn);
            RecheckAbilities();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            if (pawn?.health == null) return;
            // Defer to the game component so the grade hediff is recomputed from
            // the pawn's now-updated stats (it may still have another reserve gene).
            GameComponent_JJK.Instance?.UpdateGradeHediff(pawn);
            RecheckAbilities();
        }

        // Force TMF to re-scan all ability sources (genes/traits/hediffs) on the
        // pawn, re-granting any cursed-technique classes/trees that should be active.
        private void RecheckAbilities()
        {
            CompAbilities comp = pawn?.GetComp<CompAbilities>();
            comp?.RecheckAbilities();
        }

        // Keep only the strongest reserve gene. If the pawn carries more than one
        // reserve gene (e.g. a weaker one was already present and a stronger one was
        // just implanted), remove every reserve gene except the one with the largest
        // Max Cursed Energy offset.
        private void EnforceHighestReserveOnly()
        {
            if (pawn?.genes == null) return;

            List<Gene_CursedEnergyReserve> reserves = new List<Gene_CursedEnergyReserve>();
            foreach (Gene g in pawn.genes.GenesListForReading)
            {
                if (g is Gene_CursedEnergyReserve r) reserves.Add(r);
            }
            if (reserves.Count <= 1) return;

            Gene_CursedEnergyReserve best = null;
            float bestVal = float.NegativeInfinity;
            foreach (Gene_CursedEnergyReserve r in reserves)
            {
                float v = ReserveOffset(r);
                if (v > bestVal)
                {
                    bestVal = v;
                    best = r;
                }
            }

            foreach (Gene_CursedEnergyReserve r in reserves)
            {
                if (r != best) pawn.genes.RemoveGene(r);
            }
        }

        private static float ReserveOffset(Gene g)
        {
            List<StatModifier> offsets = g?.def?.statOffsets;
            if (offsets == null) return 0f;
            return offsets.GetStatOffsetFromList(JJK_DefOf.JJK_MaxCursedEnergy);
        }
    }
}
