using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Separate from DamageDef: armor and wounds keep their normal engine behavior.
    public class MahoragaPhenomenonDef : Def
    {
        public bool basic;
        public int spins; // 0 uses the race's maxLevel.
        public int delayTicks; // 0 uses the race's adaptDelayTicks.
        public List<string> abilities, projectiles, pawns, tools, hediffs, damageTypes;
    }

    public class MahoragaPhenomenonExtension : DefModExtension
    {
        public MahoragaPhenomenonDef phenomenon;
    }

    public static class MahoragaPhenomena
    {
        public const string Infinity = "JJK_Adapt_Infinity";
        public const string Void = "JJK_Adapt_Void";
        private static Dictionary<string, string> sources, damageTypes, controls;
        private static void EnsureCatalog()
        {
            if (sources != null) return;
            sources = new Dictionary<string, string>();
            damageTypes = new Dictionary<string, string>();
            controls = new Dictionary<string, string>();
            foreach (var d in DefDatabase<MahoragaPhenomenonDef>.AllDefsListForReading)
            {
                foreach (var list in new[] { d.abilities, d.projectiles, d.pawns, d.tools, d.hediffs })
                    if (list != null) foreach (string name in list) sources[name] = d.defName;
                if (d.damageTypes != null) foreach (string name in d.damageTypes) damageTypes[name] = d.defName;
                if (d.hediffs != null) foreach (string name in d.hediffs) controls[name] = d.defName;
            }
        }
        public static MahoragaPhenomenonDef Definition(string key) => key == null ? null : DefDatabase<MahoragaPhenomenonDef>.GetNamedSilentFail(key);
        public static string Explicit(Def def)
        {
            if (def == null) return null;
            var ext = def.GetModExtension<MahoragaPhenomenonExtension>();
            if (ext?.phenomenon != null) return ext.phenomenon.defName;
            EnsureCatalog();
            return sources.TryGetValue(def.defName, out var key) ? key : null;
        }
        public static string ForAbility(AbilityDef def) => Explicit(def) ?? (def == null ? null : "ability:" + def.defName);
        public static string ControlKey(HediffDef def)
        {
            if (def == null) return null;
            EnsureCatalog();
            return def.GetModExtension<MahoragaPhenomenonExtension>()?.phenomenon?.defName
                ?? (controls.TryGetValue(def.defName, out var key) ? key : null);
        }
        public static string Conventional(DamageDef def)
        {
            if (def == null) return null;
            EnsureCatalog();
            return damageTypes.TryGetValue(def.defName, out var key) ? key : "damage:" + def.defName;
        }
        public static string Resolve(DamageInfo hit)
        {
            // A context belongs to its actual instigator, never to the caster's genes.
            string soulCut = hit.Weapon?.GetCompProperties<CompProperties_CursedTool>()?.heavenlyRestrictionSoulFactor > 0f
                && hit.Instigator is Pawn wielder && JJK_Utils.IsHeavenlyRestricted(wielder) ? "JJK_Adapt_SoulCut" : null;
            return MahoragaExposure.ForSource(hit.Instigator) ?? Explicit(hit.Weapon) ?? soulCut
                ?? Explicit(hit.Instigator?.def) ?? Conventional(hit.Def);
        }
        public static string Label(string key)
        {
            var def = Definition(key);
            if (def != null) return def.LabelCap;
            if (key?.StartsWith("ability:") == true) return DefDatabase<AbilityDef>.GetNamedSilentFail(key.Substring(8))?.LabelCap ?? key;
            if (key?.StartsWith("projectile:") == true) return DefDatabase<ThingDef>.GetNamedSilentFail(key.Substring(11))?.LabelCap ?? key;
            if (key?.StartsWith("damage:") == true) return DefDatabase<DamageDef>.GetNamedSilentFail(key.Substring(7))?.LabelCap ?? key;
            return key ?? "";
        }
        public static bool Basic(string key) => Definition(key)?.basic == true;

        // Control exposures progress even when they cause no injury.
        public static float ControlFactor(Pawn target, string key = null)
        {
            var comp = MahoragaUtil.CompOf(target);
            key = key ?? MahoragaExposure.Current?.key;
            if (comp == null || key == null || target.Dead) return 1f;
            float factor = comp.DamageFactor(key);
            comp.NotifyHit(key);
            return factor;
        }
        public static int ControlTicks(Pawn target, int ticks, string key = null) => Mathf.RoundToInt(ticks * ControlFactor(target, key));
    }

    // Scoped attribution is restored even if another mod throws or re-enters damage.
    public sealed class MahoragaExposure : IDisposable
    {
        [ThreadStatic] public static MahoragaExposure Current;
        public readonly string key;
        public readonly Thing source;
        private readonly MahoragaExposure previous;
        private bool disposed;
        public MahoragaExposure(string key, Thing source)
        {
            previous = Current; this.key = key; this.source = source; Current = this;
        }
        public static string ForSource(Thing source) => Current != null && Current.source == source ? Current.key : null;
        public void Dispose() { if (!disposed) { Current = previous; disposed = true; } }
    }

    internal static class MahoragaDelivery
    {
        internal sealed class Payload { public string key; }
        private static readonly ConditionalWeakTable<Thing, Payload> payloads = new ConditionalWeakTable<Thing, Payload>();
        public static void Set(Thing thing, string key) { if (key != null) payloads.GetOrCreateValue(thing).key = key; }
        public static string Get(Thing thing) => payloads.TryGetValue(thing, out var p) ? p.key : null;
        public static string ProjectileKey(Projectile projectile)
        {
            var key = Get(projectile) ?? MahoragaPhenomena.Explicit(projectile.def);
            if (key != null) return key;
            Ability ability = (projectile as TaranMagicFramework.Bullet_Ability)?.ability
                ?? (projectile as TaranMagicFramework.Projectile_Explosive_Ability)?.ability;
            return ability != null ? MahoragaPhenomena.ForAbility(ability.def)
                : projectile.def.defName.StartsWith("JJK_") ? "projectile:" + projectile.def.defName : null;
        }
        public static void Expose(Thing thing)
        {
            string key = Get(thing);
            Scribe_Values.Look(ref key, "jjkMahoragaPhenomenon");
            if (Scribe.mode == LoadSaveMode.LoadingVars) Set(thing, key);
        }
    }

    [HarmonyPatch]
    public static class Patch_MahoragaAbilityScope
    {
        public static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(Ability).Assembly, typeof(MahoragaExposure).Assembly }
            .SelectMany(a => a.GetTypes()).Where(t => typeof(Ability).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "Start" && !m.IsAbstract && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool));
        public static void Prefix(Ability __instance, out MahoragaExposure __state) =>
            __state = new MahoragaExposure(MahoragaPhenomena.ForAbility(__instance.def), __instance.pawn);
        public static void Finalizer(MahoragaExposure __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_MahoragaProjectileLaunch
    {
        public static void Postfix(Projectile __instance, Thing launcher)
        {
            // Reflection changes the launcher, not the original phenomenon.
            if (MahoragaDelivery.Get(__instance) == null)
                MahoragaDelivery.Set(__instance, MahoragaExposure.ForSource(launcher) ?? MahoragaPhenomena.Explicit(__instance.def));
        }
    }

    [HarmonyPatch]
    public static class Patch_MahoragaProjectileScope
    {
        public static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(Projectile).Assembly, typeof(Ability).Assembly, typeof(MahoragaExposure).Assembly }
            .SelectMany(a => a.GetTypes()).Where(t => typeof(Projectile).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsAbstract && (m.Name == "Impact" || m.Name == "Explode" || m.Name == "Tick" || m.Name == "TickInterval"));
        public static void Prefix(Projectile __instance, out MahoragaExposure __state)
        {
            string key = MahoragaDelivery.ProjectileKey(__instance);
            __state = key == null && MahoragaExposure.Current == null ? null : new MahoragaExposure(key, __instance.Launcher);
        }
        public static void Finalizer(MahoragaExposure __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.ExposeData))]
    public static class Patch_MahoragaProjectileSave { public static void Postfix(Projectile __instance) => MahoragaDelivery.Expose(__instance); }

    [HarmonyPatch(typeof(Explosion), nameof(Explosion.StartExplosion))]
    public static class Patch_MahoragaExplosionStart
    {
        public static void Prefix(Explosion __instance) => MahoragaDelivery.Set(__instance, MahoragaExposure.ForSource(__instance.instigator));
    }
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.Tick))]
    public static class Patch_MahoragaExplosionScope
    {
        public static void Prefix(Explosion __instance, out MahoragaExposure __state) => __state = new MahoragaExposure(MahoragaDelivery.Get(__instance), __instance.instigator);
        public static void Finalizer(MahoragaExposure __state) => __state?.Dispose();
    }
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.ExposeData))]
    public static class Patch_MahoragaExplosionSave { public static void Postfix(Explosion __instance) => MahoragaDelivery.Expose(__instance); }

    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class Patch_MahoragaStun
    {
        public static bool Prefix(StunHandler __instance, ref int __0, Thing __1)
        {
            var p = __instance.parent as Pawn;
            string key = MahoragaExposure.ForSource(__1);
            // Untagged stun is its own fallback, and must not be erased by adapting to Void.
            if (key == null && MahoragaUtil.CompOf(p) != null) key = "damage:Stun";
            if (key == null) return true;
            __0 = MahoragaPhenomena.ControlTicks(p, __0, key);
            MahoragaUtil.CompOf(p)?.TrackStun(key, __0);
            return __0 > 0;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_MahoragaControlHediff
    {
        public static bool Prefix(Pawn_HealthTracker __instance, Hediff __0)
        {
            if (MahoragaUtil.CompOf(__instance.pawn) == null) return true;
            string key = MahoragaPhenomena.ControlKey(__0?.def);
            return key == null || MahoragaPhenomena.ControlFactor(__instance.pawn, key) > 0;
        }
    }
}
