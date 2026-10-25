using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    public class HediffCompProperties_DomainBurnout : HediffCompProperties
    {
        public HediffCompProperties_DomainBurnout()
        {
            compClass = typeof(HediffComp_DomainBurnout);
        }
    }

    // Records which cursed-technique tree is locked by this burnout (Section 20.5:
    // the technique used to cast the domain is unusable until the burnout fades).
    // Patch_DomainBurnout reads this to block that whole tree's abilities.
    public class HediffComp_DomainBurnout : HediffComp
    {
        public AbilityTreeDef lockedTree;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Defs.Look(ref lockedTree, "lockedTree");
        }
    }
}
