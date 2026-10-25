using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaranMagicFramework;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Soul mender replaces the RCT trait. Preserve Red/Purple prerequisites after
    // that upgrade without dropping their gene, grade, or other requirements.
    [HarmonyPatch(typeof(AcquireRequirement), "RequirementSatisfied",
        new[] { typeof(AbilityClass), typeof(Ability), typeof(string) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_RCTRequirements
    {
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");

        public static void Postfix(AcquireRequirement __instance, AbilityClass __0, Ability __1,
            ref string __2, ref bool __result)
        {
            if (__result || __instance.requiredTraits?.Contains(JJK_DefOf.JJK_RCT) != true
                || __0?.pawn?.story?.traits?.HasTrait(JJK_DefOf.JJK_SoulMender) != true) return;
            var alternative = (AcquireRequirement)Clone.Invoke(__instance, null);
            alternative.requiredTraits = __instance.requiredTraits
                .Select(t => t == JJK_DefOf.JJK_RCT ? JJK_DefOf.JJK_SoulMender : t).ToList();
            // The clone no longer contains JJK_RCT, so this cannot recursively retry.
            __result = alternative.RequirementSatisfied(__0, __1, out __2);
        }
    }
}
