using HarmonyLib;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Enforces Domain Expansion burnout (Section 20.5). After a domain ends, the
    // technique that cast it is locked until the JJK_DomainBurnout hediff fades:
    // every ability in that technique's tree has its gizmo hidden and any cast
    // attempt blocked. Mirrors Patch_TechniqueSeal, but scoped to the locked tree
    // rather than sealing all cursed techniques.
    internal static class DomainBurnoutUtil
    {
        public static bool IsLocked(Ability ability)
        {
            Pawn p = ability?.pawn;
            var trees = ability?.def?.abilityTrees;
            if (p?.health?.hediffSet == null || trees == null) return false;

            var hediffs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def != JJK_DefOf.JJK_DomainBurnout) continue;
                HediffComp_DomainBurnout comp = hediffs[i].TryGetComp<HediffComp_DomainBurnout>();
                if (comp?.lockedTree != null && trees.Contains(comp.lockedTree)) return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.ShouldShowGizmos), MethodType.Getter)]
    public static class Patch_DomainBurnout_Gizmos
    {
        public static void Postfix(Ability __instance, ref bool __result)
        {
            if (!__result) return;
            if (DomainBurnoutUtil.IsLocked(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Ability), "Cast")]
    public static class Patch_DomainBurnout_Cast
    {
        public static bool Prefix(Ability __instance)
        {
            // Returning false cancels the cast for a burnt-out technique.
            return !DomainBurnoutUtil.IsLocked(__instance);
        }
    }
}
