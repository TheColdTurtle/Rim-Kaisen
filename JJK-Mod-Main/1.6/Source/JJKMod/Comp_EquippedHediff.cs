using HarmonyLib;
using RimWorld;
using Verse;

namespace JJKMod
{
    // Applies a hediff to the pawn while this thing is held (weapon) or worn
    // (apparel), and removes it the moment the item leaves them. Used by the
    // six eyes cover (JJK_Blindfolded) and the Inverted Spear of Heaven
    // (JJK_CursedEnergySuppressed - a wielding sorcerer's own energy goes silent).
    public class CompProperties_EquippedHediff : CompProperties
    {
        public HediffDef hediff;

        public CompProperties_EquippedHediff()
        {
            compClass = typeof(Comp_EquippedHediff);
        }
    }

    public class Comp_EquippedHediff : ThingComp
    {
        public CompProperties_EquippedHediff Props => (CompProperties_EquippedHediff)props;

        public void Apply(Pawn pawn)
        {
            if (Props.hediff == null || pawn?.health == null) return;
            if (pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff) == null)
                pawn.health.AddHediff(Props.hediff);
        }

        public void Unapply(Pawn pawn)
        {
            if (Props.hediff == null) return;
            Hediff h = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Props.hediff);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        public override void Notify_Equipped(Pawn pawn) => Apply(pawn);

        public override void Notify_Unequipped(Pawn pawn) => Unapply(pawn);
    }

    // Apparel does not reliably route through ThingComp.Notify_Equipped the way
    // equipment does, so mirror the comp's apply/remove off the apparel tracker.
    // Both paths are idempotent, so double notification is harmless.
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    public static class Patch_EquippedHediff_ApparelAdded
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            apparel?.GetComp<Comp_EquippedHediff>()?.Apply(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    public static class Patch_EquippedHediff_ApparelRemoved
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            apparel?.GetComp<Comp_EquippedHediff>()?.Unapply(__instance.pawn);
        }
    }
}
