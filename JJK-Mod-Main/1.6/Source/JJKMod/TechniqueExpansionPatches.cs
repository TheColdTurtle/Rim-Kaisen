using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;

namespace JJKMod
{
    [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))]
    public static class Patch_InverseDamage
    {
        public static float Invert(float damage)=>damage>100?damage:Mathf.Clamp(60f/Mathf.Max(1,damage),2,25);
        public static void Prefix(Thing __instance,ref DamageInfo dinfo)
        {
            var p=__instance as Pawn;if(p==null)return;var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(x=>x.pawn==p);
            if(s==null || !s.Operational || dinfo.Instigator==null || dinfo.Instigator==p || !dinfo.Def.ExternalViolenceFor(p) || dinfo.Amount<=0)return;
            if(s.inverseUntil>ExpansionTech.Now&&s.Has("Inverse"))dinfo.SetAmount(Invert(dinfo.Amount));
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PostApplyDamage))]
    public static class Patch_RitualInterrupted
    {
        public static void Postfix(Pawn __instance,float totalDamageDealt)
        { if(totalDamageDealt>0){var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(x=>x.pawn==__instance);if(s!=null)s.ritualUntil=0;} }
    }
    [HarmonyPatch(typeof(Projectile),nameof(Projectile.Tick))]
    public static class Patch_SpatialProjectile
    {
        public static bool Prefix(Projectile __instance,Thing ___launcher)
        {
            var gc=GameComponent_TechniqueExpansion.Instance;
            if(gc==null||!__instance.Spawned||___launcher==null||gc.reflected.Contains(__instance))return true;
            foreach(var s in gc.states.Where(s=>s.Operational&&s.pawn.Map==__instance.Map&&___launcher.HostileTo(s.pawn)))
            {
                bool sky=s.skyUntil>ExpansionTech.Now&&s.Has("SkyManipulation");
                bool gravity=s.gravityUntil>ExpansionTech.Now&&s.Has("Antigravity");
                if((!sky&&!gravity)||s.pawn.Position.DistanceToSquared(__instance.Position)>(sky?9:16)
                    ||!GenSight.LineOfSight(s.pawn.Position,__instance.Position,__instance.Map)||!ExpansionTech.Pay(s.pawn,5))continue;
                if(sky&&___launcher.Spawned&&___launcher.Map==__instance.Map)
                {
                    gc.reflected.Add(__instance);
                    __instance.Launch(s.pawn,__instance.ExactPosition,___launcher,___launcher,ProjectileHitFlags.IntendedTarget);
                }
                else __instance.Destroy();
                CombatPresentation.Play("Impact",s.pawn.Map,s.pawn.Position);return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(TradeDeal),nameof(TradeDeal.TryExecute))]
    public static class Patch_TradeReceipts
    {
        public static void Postfix(bool __result,bool __0)
        {
            var p=TradeSession.playerNegotiator;
            if(!__result||!__0||p?.Spawned!=true)return;
            var s=ExpansionTech.State(p);if(s.receiptNext>ExpansionTech.Now)return;
            if(ExpansionTech.Place(p,new[]{"JJK_ReceiptKnives","JJK_ReceiptWeight","JJK_ReceiptRest"}.RandomElement()))s.receiptNext=ExpansionTech.Now+60000;
        }
    }
    // Only TMF acquisition checks see a borrowed gene as satisfying the original
    // gene requirement. Biological ownership, inheritance and manual choices do not.
    [HarmonyPatch(typeof(AcquireRequirement),nameof(AcquireRequirement.RequirementSatisfied),new[]{typeof(AbilityClass),typeof(TaranMagicFramework.Ability),typeof(string)},
        new[]{ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Out})]
    public static class Patch_CopyRequirementScope
    {
        [ThreadStatic] public static Pawn checking;
        public static void Prefix(AbilityClass __0,out Pawn __state){__state=checking;checking=__0.pawn;}
        public static void Finalizer(Pawn __state){checking=__state;}
    }
    [HarmonyPatch(typeof(Pawn_GeneTracker),nameof(Pawn_GeneTracker.HasActiveGene))]
    public static class Patch_CopyRequirementGene
    {
        public static void Postfix(Pawn_GeneTracker __instance,GeneDef __0,ref bool __result)
        {
            var p=Patch_CopyRequirementScope.checking;if(__result||p==null||p.genes!=__instance)return;
            var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(x=>x.pawn==p);
            if(s?.selectedCopy==__0&&s.borrowed!=null&&s.RikaActive)__result=true;
        }
    }
}

