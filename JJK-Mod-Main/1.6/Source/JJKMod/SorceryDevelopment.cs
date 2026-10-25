using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public enum DomainRefinement { None, Condensed, Fortified, Economical }

    public class SorceryRecord : IExposable
    {
        public Pawn pawn, protectedPawn, revealedTo;
        public bool protectionBroken;
        public int revealUntil, revealCooldown, nextTrack, nextConfrontation, resolve, rescues;
        public int rescueDeadline;
        public List<AbilityTreeDef> domainTrees = new List<AbilityTreeDef>();
        public List<DomainRefinement> refinements = new List<DomainRefinement>();
        public List<ShikigamiDef> donors = new List<ShikigamiDef>(), heirs = new List<ShikigamiDef>();
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref protectedPawn, "protectedPawn");
            Scribe_References.Look(ref revealedTo, "revealedTo");
            Scribe_Values.Look(ref protectionBroken, "protectionBroken");
            Scribe_Values.Look(ref revealUntil, "revealUntil");
            Scribe_Values.Look(ref revealCooldown, "revealCooldown");
            Scribe_Values.Look(ref nextTrack, "nextTrack");
            Scribe_Values.Look(ref nextConfrontation, "nextConfrontation");
            Scribe_Values.Look(ref resolve, "resolve");
            Scribe_Values.Look(ref rescues, "rescues");
            Scribe_Values.Look(ref rescueDeadline, "rescueDeadline");
            Scribe_Collections.Look(ref domainTrees, "domainTrees", LookMode.Def);
            Scribe_Collections.Look(ref refinements, "refinements", LookMode.Value);
            Scribe_Collections.Look(ref donors, "donors", LookMode.Def);
            Scribe_Collections.Look(ref heirs, "heirs", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                domainTrees = domainTrees ?? new List<AbilityTreeDef>();
                refinements = refinements ?? new List<DomainRefinement>();
                donors = donors ?? new List<ShikigamiDef>(); heirs = heirs ?? new List<ShikigamiDef>();
            }
        }
        public DomainRefinement Refinement(AbilityTreeDef tree)
        { int i = domainTrees.IndexOf(tree); return i >= 0 && i < refinements.Count ? refinements[i] : DomainRefinement.None; }
        public bool Refine(AbilityTreeDef tree, DomainRefinement choice)
        {
            if (tree == null || choice == DomainRefinement.None || !Enum.IsDefined(typeof(DomainRefinement), choice) || Refinement(tree) != DomainRefinement.None
                || GameComponent_JJK.Instance.GetTechniqueXp(pawn, tree) < 1000
                || pawn.MapHeld?.GetComponent<MapComponent_DomainManager>().HasDomain(pawn) == true
                || pawn.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques)?.UnlockedTrees.Contains(tree) != true
                || !DefDatabase<TaranMagicFramework.AbilityDef>.AllDefsListForReading.Any(a => a.abilityTrees?.Contains(tree) == true
                    && a.GetModExtension<DomainExtension>() is DomainExtension d && !d.isSimpleDomain && (choice != DomainRefinement.Fortified || d.closed))) return false;
            domainTrees.Add(tree); refinements.Add(choice); return true;
        }
        public bool ProtectionActive => !protectionBroken && protectedPawn != null && !protectedPawn.Dead
            && pawn.Spawned && protectedPawn.Spawned && pawn.Map == protectedPawn.Map
            && pawn.Position.DistanceToSquared(protectedPawn.Position) <= 144;
    }

    public class GameComponent_SorceryDevelopment : GameComponent
    {
        public static GameComponent_SorceryDevelopment Instance => Current.Game?.GetComponent<GameComponent_SorceryDevelopment>();
        public List<SorceryRecord> records = new List<SorceryRecord>();
        public GameComponent_SorceryDevelopment(Game game) { }
        public SorceryRecord For(Pawn pawn, bool create = true)
        {
            if (pawn == null) return null;
            var r = records.FirstOrDefault(x => x.pawn == pawn);
            if (r == null && create) { r = new SorceryRecord { pawn = pawn }; records.Add(r); }
            return r;
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "sorceryDevelopment", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { records = records ?? new List<SorceryRecord>(); records.RemoveAll(r => r?.pawn == null); }
        }
        public override void GameComponentTick()
        {
            if (SorceryDevelopment.Now % 60 != 0) return;
            foreach (var map in Find.Maps) foreach (var p in map.mapPawns.AllPawnsSpawned)
                p.equipment?.Primary?.TryGetComp<Comp_CursedTool>()?.Wield(p, 60);
            foreach (var r in records.ToList())
            {
                if (r.pawn.Dead || r.pawn.Destroyed) continue;
                if (SorceryDevelopment.Has(r.pawn, "JJK_Vow_Protector") && !r.protectionBroken
                    && (r.protectedPawn == null || r.protectedPawn.Dead || r.protectedPawn.Destroyed || r.protectedPawn.Faction != r.pawn.Faction))
                    SorceryDevelopment.BreakProtection(r);
                VesselDevelopment.Tick(r);
            }
            if (SorceryDevelopment.Now % 60000 == 0) records.RemoveAll(r => r.pawn.Destroyed);
        }
    }

    public static class SorceryDevelopment
    {
        public static int Now => Find.TickManager.TicksGame;
        public static SorceryRecord State(Pawn p) => GameComponent_SorceryDevelopment.Instance?.For(p);
        public static bool Has(Pawn p, string def) => p?.health?.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed(def)) == true;
        public static bool CanAct(Pawn p) => p?.Spawned == true && !p.Dead && !p.Downed && !p.InMentalState && p.Faction == Faction.OfPlayer;
        public static bool CanSpeak(Pawn p) => p?.health?.capacities.GetLevel(PawnCapacityDefOf.Talking) >= .5f;
        public static bool Spend(Pawn p, float amount)
        {
            var ce = p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (ce == null || ce.energy < amount || TechniqueSealUtil.IsSealed(p)) return false;
            ce.energy -= amount; return true;
        }
        public static float OutputFactor(Pawn p)
        {
            var r = GameComponent_SorceryDevelopment.Instance?.For(p, false);
            float factor = 1;
            if (Has(p, "JJK_Vow_Protector")) factor *= r?.protectionBroken == true ? .75f : r?.ProtectionActive == true ? 1.3f : 1f;
            if (r != null && r.revealUntil > Now) factor *= 1.2f;
            return factor;
        }
        public static void BreakProtection(SorceryRecord r)
        {
            if (r.protectionBroken) return;
            r.protectionBroken = true;
            r.pawn.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_ProtectionFailed"));
            Messages.Message("JJK_DeepProtectionBroken".Translate(r.pawn.LabelShortCap), r.pawn, MessageTypeDefOf.NegativeEvent);
        }
        public static bool Reveal(Pawn p, Pawn enemy)
        {
            var r = State(p);
            if (!CanAct(p) || !CanSpeak(p) || !JJK_Utils.HasCursedTechnique(p) || r == null || Now < r.revealCooldown
                || enemy?.Spawned != true || enemy.Dead || enemy.Downed || !enemy.RaceProps.Humanlike
                || enemy.health.capacities.GetLevel(PawnCapacityDefOf.Hearing) < .5f
                || !enemy.HostileTo(p) || enemy.Map != p.Map || p.Position.DistanceToSquared(enemy.Position) > 144
                || !GenSight.LineOfSight(p.Position, enemy.Position, p.Map)) return false;
            r.revealedTo = enemy; r.revealUntil = Now + 1800; r.revealCooldown = Now + 6000;
            Messages.Message("JJK_DeepRevealed".Translate(p.LabelShortCap, enemy.LabelShortCap), p, MessageTypeDefOf.CautionInput);
            return true;
        }
        public static float CounterFactor(Pawn attacker, Pawn victim)
        {
            var a = GameComponent_SorceryDevelopment.Instance?.For(attacker, false);
            var v = GameComponent_SorceryDevelopment.Instance?.For(victim, false);
            float factor = a != null && a.revealUntil > Now && a.revealedTo == victim ? .7f : 1f;
            if (v != null && v.revealUntil > Now && v.revealedTo == attacker) factor *= 1.25f;
            return factor;
        }
        public static float Range(Ability a)
        {
            float range = a.AbilityTier?.verbProperties?.FirstOrDefault()?.range ?? a.AbilityTier?.effectRadius ?? 0;
            return range * .6f;
        }
        public static bool Allowed(Ability a, LocalTargetInfo target)
        {
            if (a?.abilityResource?.def != JJK_DefOf.JJK_CursedEnergy) return true;
            if (Has(a.pawn, "JJK_Vow_Spoken") && !CanSpeak(a.pawn)) return false;
            if (!Has(a.pawn, "JJK_Vow_CloseRange") || !target.IsValid || target.Thing == a.pawn) return true;
            float range = Range(a);
            return range <= 0 || a.pawn.Position.DistanceToSquared(target.Cell) <= range * range;
        }
    }
    public class StatPart_SorceryDevelopment : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        { if (req.Thing is Pawn p) val *= SorceryDevelopment.OutputFactor(p); }
        public override string ExplanationPart(StatRequest req) => req.Thing is Pawn p
            ? "JJK_DeepOutput".Translate(SorceryDevelopment.OutputFactor(p).ToStringPercent()) : null;
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.CanHitTarget))]
    public static class Patch_DeepRange
    {
        public static void Postfix(Ability __instance, LocalTargetInfo target, ref bool __result)
        { __result &= SorceryDevelopment.Allowed(__instance, target); }
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.DoCast))]
    public static class Patch_DeepCastGate
    {
        public static bool Prefix(Ability __instance, LocalTargetInfo target) => SorceryDevelopment.Allowed(__instance, target);
    }
    [HarmonyPatch]
    public static class Patch_DeepStartGate
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] { typeof(Ability).Assembly, typeof(SorceryDevelopment).Assembly }
            .SelectMany(a => a.GetTypes()).Where(t => typeof(Ability).IsAssignableFrom(t))
            .Select(t => t.GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly,
                null, new[] { typeof(bool) }, null)).Where(m => m != null);
        public static bool Prefix(Ability __instance) => SorceryDevelopment.Allowed(__instance, __instance.curTarget);
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.IsInstantAction), MethodType.Getter)]
    public static class Patch_DeepPreparationRequired
    {
        public static void Postfix(Ability __instance, ref bool __result)
        { if (__instance.abilityResource?.def == JJK_DefOf.JJK_CursedEnergy && SorceryDevelopment.Has(__instance.pawn, "JJK_Vow_Spoken")) __result = false; }
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.CanBeActivated))]
    public static class Patch_DeepVoiceAvailability
    {
        public static void Postfix(Ability __instance, ref bool __result, ref string failReason)
        {
            if (__instance.abilityResource?.def == JJK_DefOf.JJK_CursedEnergy && SorceryDevelopment.Has(__instance.pawn, "JJK_Vow_Spoken") && !SorceryDevelopment.CanSpeak(__instance.pawn))
            { __result = false; failReason = "JJK_DeepVoiceRequired".Translate(); }
        }
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.CastTicks), MethodType.Getter)]
    public static class Patch_DeepSpoken
    {
        public static void Postfix(Ability __instance, ref int __result)
        { if (__instance.abilityResource?.def == JJK_DefOf.JJK_CursedEnergy && SorceryDevelopment.Has(__instance.pawn, "JJK_Vow_Spoken")) __result += 180; }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_DeepDamage
    {
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (__instance is Pawn victim && dinfo.Instigator is Pawn attacker && dinfo.Amount > 0)
            {
                dinfo.SetAmount(dinfo.Amount * SorceryDevelopment.CounterFactor(attacker, victim));
            }
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_DeepProtectionDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            // Armor, Infinity and other defenses may absorb an attempted hit.
            // The vow promises no inflicted damage, so only an actual injury breaks it.
            if (totalDamageDealt <= 0 || !(dinfo.Instigator is Pawn attacker)) return;
            var r = GameComponent_SorceryDevelopment.Instance?.For(attacker, false);
            if (r?.protectedPawn == __instance && SorceryDevelopment.Has(attacker, "JJK_Vow_Protector"))
                SorceryDevelopment.BreakProtection(r);
        }
    }
}
