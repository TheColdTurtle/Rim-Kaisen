using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JJKMod
{
    // These are colony philosophies inspired by JJK, not innate powers or conversions.
    public enum JujutsuEthic { Guardian, Reform, Clan, Supremacy, Sovereign, Evolution }
    public class JujutsuEthicExtension : DefModExtension { public JujutsuEthic ethic; }

    public static class JujutsuIdeology
    {
        public static bool Sorcerer(Pawn p) => p?.RaceProps.Humanlike == true
            && p.genes != null && p.genes.GenesListForReading.Any(g => g.Active && g is Gene_CursedEnergyReserve)
            && JJK_GradeUtility.IsSorcerer(p);
        public static bool Strong(Pawn p) => p != null && (JJK_Utils.CanPhaseDomainBarrier(p)
            || (Sorcerer(p) && GameComponent_JJK.Instance?.GetGrade(p) >= SorcererGrade.Grade1)
            || (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) >= 12);
        public static bool Follows(Pawn p, JujutsuEthic ethic) => ModsConfig.IdeologyActive
            && p?.Ideo?.memes.Any(m => m.GetModExtension<JujutsuEthicExtension>()?.ethic == ethic) == true;
        public static void Record(Pawn p, string name)
        {
            if (!ModsConfig.IdeologyActive || p == null || p.Dead || p.needs?.mood == null || p.Ideo == null) return;
            var ev = DefDatabase<HistoryEventDef>.GetNamedSilentFail("JJK_Ideo_" + name);
            if (ev != null) Find.HistoryEventsManager.RecordEvent(new HistoryEvent(ev, p.Named(HistoryEventArgsNames.Doer)));
        }
        public static Pawn Leader(Pawn p) => p?.Ideo?.PreceptsListForReading.OfType<Precept_Role>()
            .FirstOrDefault(r => r.def.leaderRole)?.ChosenPawnSingle();
    }

    public class ThoughtWorker_JujutsuEthic : ThoughtWorker_Precept
    {
        public override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!ModsConfig.IdeologyActive || !p.Spawned || !p.IsFreeColonist) return false;
            var ethic = def.GetModExtension<JujutsuEthicExtension>().ethic;
            switch (ethic)
            {
                case JujutsuEthic.Guardian:
                    return p.Map.mapPawns.FreeColonistsSpawned.Any(o => o != p && !JujutsuIdeology.Sorcerer(o));
                case JujutsuEthic.Clan:
                    var leader = JujutsuIdeology.Leader(p);
                    if (leader == null) return false; // Vanilla already handles an empty role.
                    return ThoughtState.ActiveAtStage(JujutsuIdeology.Sorcerer(leader)
                        && GameComponent_JJK.Instance.GetGrade(leader) >= SorcererGrade.Grade1 ? 1 : 0);
                case JujutsuEthic.Supremacy:
                    var colonists = p.Map.mapPawns.FreeColonistsSpawned;
                    if (colonists.Count < 2) return false;
                    return ThoughtState.ActiveAtStage(colonists.Any(o => !JujutsuIdeology.Sorcerer(o)) ? 0 : 1);
                default: return false;
            }
        }
    }

    public class ThoughtWorker_JujutsuRespect : ThoughtWorker_Precept_Social
    {
        public override ThoughtState ShouldHaveThought(Pawn p, Pawn otherPawn)
        {
            if (!ModsConfig.IdeologyActive || otherPawn == null || p == otherPawn
                || !otherPawn.RaceProps.Humanlike || !RelationsUtility.PawnsKnowEachOther(p, otherPawn)) return false;
            switch (def.GetModExtension<JujutsuEthicExtension>().ethic)
            {
                case JujutsuEthic.Supremacy: return ThoughtState.ActiveAtStage(JujutsuIdeology.Sorcerer(otherPawn) ? 1 : 0);
                case JujutsuEthic.Clan:
                    return ThoughtState.ActiveAtStage(JujutsuIdeology.Sorcerer(otherPawn)
                        && JJK_Utils.HasCursedTechnique(otherPawn) ? 1 : 0);
                case JujutsuEthic.Sovereign: return JujutsuIdeology.Strong(otherPawn);
                default: return false;
            }
        }
    }

    [HarmonyPatch(typeof(JujutsuTraining), nameof(JujutsuTraining.Complete))]
    public static class Patch_IdeoTraining
    {
        public static void Postfix(TrainingPlan plan, int __result)
        {
            if (__result <= 0 || plan?.mentor == null) return;
            JujutsuIdeology.Record(plan.pupil, "Mentorship");
            JujutsuIdeology.Record(plan.mentor, "Mentorship");
        }
    }
    [HarmonyPatch(typeof(GameComponent_Jujutsu), nameof(GameComponent_Jujutsu.Claim))]
    public static class Patch_IdeoService
    {
        public static void Postfix(JujutsuContract c, bool __result)
        {
            if (!__result || c?.map == null) return;
            foreach (var p in c.map.mapPawns.FreeColonistsSpawned)
                JujutsuIdeology.Record(p, "Service");
        }
    }
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class Patch_IdeoResearch
    {
        public static void Prefix(ResearchProjectDef proj, out bool __state) => __state = proj != null && !proj.IsFinished;
        public static void Postfix(ResearchProjectDef proj, bool __state)
        {
            if (!ModsConfig.IdeologyActive || !__state || !proj.IsFinished || !proj.defName.StartsWith("JJK_")) return;
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
                foreach (var p in map.mapPawns.FreeColonistsSpawned) JujutsuIdeology.Record(p, "Discovery");
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_IdeoExorcism
    {
        public struct DeathContext { public Pawn killer; public bool curse, worthy; }
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out DeathContext __state)
        {
            __state = default(DeathContext);
            if (!ModsConfig.IdeologyActive || __instance.Dead || !(dinfo?.Instigator is Pawn attacker)) return;
            var killer = ExpansionTech.EnergyOwner(attacker);
            if (killer == null || killer == __instance || !__instance.HostileTo(killer)) return;
            __state.killer = killer;
            __state.curse = JJK_Utils.IsCursedSpirit(__instance) && __instance.Faction != killer.Faction;
            __state.worthy = !__instance.Downed && !__instance.IsPrisoner
                && __instance.kindDef.combatPower >= System.Math.Max(100f, killer.kindDef.combatPower);
        }
        public static void Postfix(Pawn __instance, DeathContext __state)
        {
            if (!__instance.Dead || __state.killer == null) return;
            if (__state.curse) JujutsuIdeology.Record(__state.killer, "Exorcism");
            if (__state.worthy) JujutsuIdeology.Record(__state.killer, "Victory");
        }
    }
}
