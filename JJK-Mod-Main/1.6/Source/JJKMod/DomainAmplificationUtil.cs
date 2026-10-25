using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaranMagicFramework;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    internal static class DomainAmplificationUtil
    {
        private static HashSet<AbilityTreeDef> innateTrees;

        public static bool Active(Pawn pawn) => !TechniqueSealUtil.IsSealed(pawn)
            && pawn?.health?.hediffSet.HasHediff(JJK_DefOf.JJK_DomainAmplification) == true;

        public static bool IsInnate(Ability ability)
        {
            if (innateTrees == null)
            {
                innateTrees = new HashSet<AbilityTreeDef>();
                foreach (GeneDef gene in DefDatabase<GeneDef>.AllDefsListForReading)
                {
                    if (gene.geneClass == null || !typeof(Gene_CursedTechnique).IsAssignableFrom(gene.geneClass)) continue;
                    var trees = gene.GetModExtension<AbilityExtension>()?.abilityTrees;
                    if (trees != null) innateTrees.UnionWith(trees);
                }
            }
            return ability?.def?.abilityTrees?.Any(innateTrees.Contains) == true;
        }

        public static bool Blocks(Ability ability) => Active(ability?.pawn) && IsInnate(ability);

        public static void EndInnateToggles(Pawn pawn)
        {
            var abilities = pawn?.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques)?.LearnedAbilities;
            if (abilities == null) return;
            foreach (Ability_Toggleable ability in abilities.OfType<Ability_Toggleable>().ToList())
                if (ability.Active && IsInnate(ability)) ability.End();
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.ShouldShowGizmos), MethodType.Getter)]
    public static class Patch_Amplification_Gizmos
    {
        public static void Postfix(Ability __instance, ref bool __result)
        {
            if (DomainAmplificationUtil.Blocks(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Ability), "Cast")]
    public static class Patch_Amplification_Cast
    {
        public static bool Prefix(Ability __instance) => !DomainAmplificationUtil.Blocks(__instance) && CopyTechnique.Allowed(__instance);
    }

    // Also catch a cast job which began before amplification was activated.
    [HarmonyPatch(typeof(Ability), "DoCast")]
    public static class Patch_Amplification_DoCast
    {
        public static bool Prefix(Ability __instance) => !DomainAmplificationUtil.Blocks(__instance)
            && !TechniqueSealUtil.IsSealed(__instance?.pawn) && !DomainBurnoutUtil.IsLocked(__instance) && CopyTechnique.Allowed(__instance);
    }

    [HarmonyPatch]
    public static class Patch_JJK_CompleteCastRestrictions
    {
        public static IEnumerable<MethodBase> TargetMethods() => typeof(Ability_DomainAmplification).Assembly.GetTypes()
            .Where(t => typeof(Ability).IsAssignableFrom(t))
            .Select(t => t.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m != null);

        public static bool Prefix(Ability __instance) => !DomainAmplificationUtil.Blocks(__instance)
            && !TechniqueSealUtil.IsSealed(__instance?.pawn) && !DomainBurnoutUtil.IsLocked(__instance)
            && CopyTechnique.Allowed(__instance)
            && (!(__instance is Ability_CursedSpeech) || CursedSpeechUtil.TalkingFactor(__instance.pawn) > 0.01f);
    }
}

