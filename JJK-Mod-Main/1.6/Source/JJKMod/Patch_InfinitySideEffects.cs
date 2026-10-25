using HarmonyLib;
using RimWorld;
using Verse;

namespace JJKMod
{
    // Infinity isn't merely damage immunity - nothing actually touches the pawn, so
    // the on-hit side effects that bypass PreApplyDamage must be suppressed too.

    // No stagger / slowdown from being "hit" (bullet impacts, melee, explosions all
    // funnel through StaggerHandler.StaggerFor; its parent field is the Pawn).
    [HarmonyPatch(typeof(StaggerHandler), nameof(StaggerHandler.StaggerFor))]
    public static class Patch_Infinity_NoStagger
    {
        public static bool Prefix(StaggerHandler __instance)
        {
            return !InfinityUtil.InfinityActive(__instance?.parent);
        }
    }

    // No gas exposure (tox gas, rot stink, etc.) while Infinity holds the world at
    // a halt - the gas can't reach the pawn either.
    [HarmonyPatch(typeof(GasUtility), nameof(GasUtility.PawnGasEffectsTickInterval))]
    public static class Patch_Infinity_NoGas
    {
        public static bool Prefix(Pawn __0)
        {
            return !InfinityUtil.InfinityActive(__0);
        }
    }
}
