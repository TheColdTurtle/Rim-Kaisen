using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // A cursed-technique gene. The gene grants its technique tree to TMF through a
    // TaranMagicFramework.AbilityExtension modExtension, but TMF only picks that up
    // when it re-scans the pawn's ability sources. Adding a gene at runtime (e.g.
    // via the dev "add gene" tool, or gene implantation) does NOT trigger that scan
    // on its own, so the technique gizmos never appear. Forcing RecheckAbilities on
    // add/remove makes the tree register (and clean up) immediately.
    public class Gene_CursedTechnique : Gene
    {
        public override void PostAdd()
        {
            base.PostAdd();
            RecheckAbilities();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            RecheckAbilities();
        }

        private void RecheckAbilities()
        {
            pawn?.GetComp<CompAbilities>()?.RecheckAbilities();
        }
    }
}
