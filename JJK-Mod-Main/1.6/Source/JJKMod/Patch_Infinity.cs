using HarmonyLib;
using RimWorld;
using Verse;

namespace JJKMod
{
    internal static class InfinityUtil
    {
        // Infinity is "up" when the pawn has the marker hediff and is not sealed.
        public static bool InfinityActive(Pawn pawn)
        {
            HediffSet set = pawn?.health?.hediffSet;
            return set != null
                && set.GetFirstHediffOfDef(JJK_DefOf.JJK_Infinity) != null
                && !TechniqueSealUtil.IsSealed(pawn)
                && !DomainAmplificationUtil.Active(pawn);
        }

        // Two things reach a Limitless user through Infinity:
        //  - the Inverted Spear of Heaven (its bypassesInfinity flag), and
        //  - an attacker running DOMAIN AMPLIFICATION, which sheathes them in their own
        //    domain and nullifies the technique holding the gap open on contact. This is
        //    the canon counter to Infinity, and the reason the technique is worth its
        //    upkeep even though it shields only the user.
        public static bool CanPierceInfinity(DamageInfo dinfo)
        {
            ThingDef weapon = dinfo.Weapon;
            if (weapon != null)
            {
                CompProperties_CursedTool props = weapon.GetCompProperties<CompProperties_CursedTool>();
                if (props != null && props.bypassesInfinity) return true;
            }

            if (dinfo.Instigator is Pawn attacker
                && DomainAmplificationUtil.Active(attacker))
            {
                return true;
            }
            return false;
        }
    }

    // Infinity's defence. While active, NOTHING reaches the pawn - every incoming
    // damage event (melee, ranged, explosions including the pawn's own Red, fire,
    // friendly fire, anything) is nullified. The sole exception is the Inverted
    // Spear of Heaven. The pawn can still freely CAST its own techniques (that is
    // not blocked here - this only guards damage arriving AT the pawn). Infinity
    // does not stop internal harm that never routes through here (starvation,
    // disease), and its heavy per-tick upkeep collapses it fast without the Six
    // Eyes' efficiency to pay for it.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PreApplyDamage))]
    public static class Patch_Infinity
    {
        public static bool Prefix(Pawn_HealthTracker __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            Pawn pawn = __instance.pawn;
            if (!InfinityUtil.InfinityActive(pawn)) return true;

            // The Inverted Spear of Heaven cuts through Infinity; nothing else does.
            if (InfinityUtil.CanPierceInfinity(dinfo)) return true;

            // A Domain Expansion's sure-hit also pierces Infinity: while trapped
            // inside a hostile domain, its caster's and summons' attacks land.
            if (dinfo.Instigator is Pawn instigator)
            {
                // Only Mahoraga's own physical contact learns and eventually crosses Infinity.
                var wheel = MahoragaUtil.CompOf(instigator);
                if (wheel != null && dinfo.Def?.isRanged == false && instigator.Spawned && pawn.Spawned
                    && instigator.Map == pawn.Map && instigator.Position.AdjacentTo8WayOrInside(pawn.Position))
                {
                    if (wheel.IsImmune(MahoragaPhenomena.Infinity)) return true;
                    wheel.NotifyHit(MahoragaPhenomena.Infinity);
                }
                MapComponent_DomainManager mgr = pawn.Map?.GetComponent<MapComponent_DomainManager>();
                if (mgr != null && mgr.IsHostileDomainAttack(pawn, instigator)) return true;
            }

            absorbed = true;
            Block(pawn);
            return false;
        }

        private static void Block(Pawn pawn)
        {
            if (pawn.Spawned && pawn.Map != null)
            {
                FleckMaker.Static(pawn.Position, pawn.Map, FleckDefOf.PsycastSkipFlashEntry, 0.5f);
            }
        }
    }

    // Infinity also refuses to let the pawn catch fire in the first place.
    // __0 reads the first argument (the Thing) positionally, independent of its
    // parameter name; the explicit signature pins the right overload.
    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryAttachFire),
        new[] { typeof(Thing), typeof(float), typeof(Thing) })]
    public static class Patch_Infinity_FireAttach
    {
        public static bool Prefix(Thing __0)
        {
            return !(__0 is Pawn pawn && InfinityUtil.InfinityActive(pawn));
        }
    }
}
