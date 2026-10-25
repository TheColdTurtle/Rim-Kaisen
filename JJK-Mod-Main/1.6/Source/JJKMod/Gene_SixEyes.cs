using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace JJKMod
{
    // Six Eyes (Section 12). The efficiency bypass (near-zero cursed energy cost),
    // +50% CT output and faster regen are applied through the gene's stats in XML;
    // the blue-eyed look is the gene's render nodes. This class additionally grants
    // the actual ocular organ: a "six eyes" hediff installed on each eye that
    // massively amplifies sight.
    public class Gene_SixEyes : Gene
    {
        public static bool HasSixEyes(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i] is Gene_SixEyes) return true;
            }
            return false;
        }

        public override void PostAdd()
        {
            base.PostAdd();
            InstallSixEyes();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            RemoveSixEyes();
        }

        private void InstallSixEyes()
        {
            if (pawn?.health == null || pawn.RaceProps?.body == null) return;
            foreach (BodyPartRecord eye in pawn.RaceProps.body.GetPartsWithDef(BodyPartDefOf.Eye))
            {
                if (pawn.health.hediffSet.PartIsMissing(eye)) continue;
                bool already = pawn.health.hediffSet.hediffs
                    .Any(h => h.def == JJK_DefOf.JJK_SixEyesOrgan && h.Part == eye);
                if (!already) pawn.health.AddHediff(JJK_DefOf.JJK_SixEyesOrgan, eye);
            }
        }

        private void RemoveSixEyes()
        {
            if (pawn?.health == null) return;
            List<Hediff> toRemove = pawn.health.hediffSet.hediffs
                .Where(h => h.def == JJK_DefOf.JJK_SixEyesOrgan).ToList();
            foreach (Hediff h in toRemove) pawn.health.RemoveHediff(h);
        }
    }
}
