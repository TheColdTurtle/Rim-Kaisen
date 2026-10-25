using HarmonyLib;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Enforces the "cursed energy goes silent" hediffs: Technique Sealed
    // (struck by the Inverted Spear of Heaven) and Cursed Energy Suppressed
    // (WIELDING the Inverted Spear - the non-sorcerer's weapon silences its
    // bearer's own energy). Either way, the ability gizmos are hidden and any
    // cast attempt is blocked. The Culling Game inactivity seal shares this gate.
    internal static class TechniqueSealUtil
    {
        public static bool IsSealed(Pawn p)
        {
            if (p?.health?.hediffSet == null) return false;
            return p.health.hediffSet.HasHediff(JJK_DefOf.JJK_TechniqueSealed)
                || p.health.hediffSet.HasHediff(JJK_DefOf.JJK_CursedEnergySuppressed)
                || p.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_CullingStagnation"));
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.ShouldShowGizmos), MethodType.Getter)]
    public static class Patch_TechniqueSeal_Gizmos
    {
        public static void Postfix(Ability __instance, ref bool __result)
        {
            if (!__result) return;
            if (TechniqueSealUtil.IsSealed(__instance?.pawn)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Ability), "Cast")]
    public static class Patch_TechniqueSeal_Cast
    {
        public static bool Prefix(Ability __instance)
        {
            // Returning false cancels the cast for a sealed pawn.
            return !TechniqueSealUtil.IsSealed(__instance?.pawn);
        }
    }
}
