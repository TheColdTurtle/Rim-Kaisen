using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JJKMod
{
    public class RivalSorcerer : IExposable
    {
        public Pawn pawn, rememberedOpponent;
        public DamageDef rememberedDamage;
        public int encounters, returnAt, arrivedAt, healAt, tactics;
        public bool retreating, ended;
        public string Tactic => tactics == 0 ? "duel the remembered opponent" : tactics == 1 ? "attack vulnerable support pawns" : "sabotage curtain anchors";
        public string Description => (pawn?.LabelShortCap ?? "Lost rival") + " — " + (ended ? "Rivalry ended" : retreating ? "Retreating" : pawn?.Spawned == true ? "On the battlefield" : "Recovering")
            + "; encounters: " + encounters + "; tactic: " + Tactic + "; remembers: " + (rememberedOpponent?.LabelShortCap ?? "no opponent yet")
            + (rememberedDamage == null ? "" : " (" + rememberedDamage.label + ")");
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "rivalPawn"); Scribe_References.Look(ref rememberedOpponent, "rememberedOpponent");
            Scribe_Defs.Look(ref rememberedDamage, "rememberedDamage"); Scribe_Values.Look(ref encounters, "encounters");
            Scribe_Values.Look(ref returnAt, "returnAt"); Scribe_Values.Look(ref arrivedAt, "arrivedAt");
            Scribe_Values.Look(ref healAt, "healAt"); Scribe_Values.Look(ref tactics, "tactics");
            Scribe_Values.Look(ref retreating, "retreating"); Scribe_Values.Look(ref ended, "ended");
        }
    }
    public static class RivalWorld
    {
        public static bool Spawn(GameComponent_ColonyLife gc, Map map, RivalSorcerer rival = null)
        {
            Faction faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.defeated
                && f.def.defName == "JJK_CurseUsers" && f.HostileTo(Faction.OfPlayer));
            if (faction == null || map == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 cell, map, CellFinder.EdgeRoadChance_Animal)) return false;
            if (rival == null)
            {
                rival = new RivalSorcerer { pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    DefDatabase<PawnKindDef>.GetNamed("JJK_RecurringRival"), faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false)) };
                gc.rivals.Add(rival);
                GenSpawn.Spawn(rival.pawn, cell, map);
            }
            else
            {
                if (!gc.HeldRivals.Contains(rival.pawn) || rival.pawn.Dead || rival.pawn.Downed) return false;
                if (!gc.HeldRivals.TryDrop(rival.pawn, cell, map, ThingPlaceMode.Near, out _)) return false;
                GameComponent_JJK.Instance.AddGradeXp(rival.pawn, 100);
                var ac = JujutsuTraining.Class(rival.pawn);
                if (ac != null) foreach (var tree in ac.UnlockedTrees.ToList()) GameComponent_JJK.Instance.AddTechniqueXp(rival.pawn, tree, 150);
                var ce = rival.pawn.GetComp<TaranMagicFramework.CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
                if (ce != null) ce.energy = ce.MaxEnergy;
            }
            rival.encounters++; rival.tactics = (rival.encounters - 1) % 3; rival.retreating = false;
            rival.arrivedAt = Find.TickManager.TicksGame;
            int hostileClans = gc.clans.Count(c => c.standing <= -30), friendlyClans = gc.clans.Count(c => c.standing >= 30);
            int support = Mathf.Clamp((rival.encounters - 1) * 50 + hostileClans * 60 - friendlyClans * 40, 0, 300);
            if (support > 0) CursedSpiritUtil.SpawnPack(map, cell, support, 4);
            Find.LetterStack.ReceiveLetter(rival.encounters == 1 ? "A rival sorcerer arrives" : "Your rival returns",
                rival.Description + ". They may try to escape when injured. Stop their retreat to end the rivalry.", LetterDefOf.ThreatBig, rival.pawn);
            return true;
        }
        public static bool Escape(GameComponent_ColonyLife gc, RivalSorcerer r)
        {
            Pawn p = r?.pawn;
            if (p?.Spawned != true || p.Dead || p.Downed || !p.Position.OnEdge(p.Map) || !r.retreating
                || p.IsPrisoner || !p.HostileTo(Faction.OfPlayer)) return false;
            p.GetLord()?.Notify_PawnLost(p, Verse.AI.Group.PawnLostCondition.ExitedMap);
            p.DeSpawn(); gc.HeldRivals.TryAdd(p);
            r.retreating = false; r.returnAt = Find.TickManager.TicksGame + 600000; r.healAt = Find.TickManager.TicksGame + 60000;
            Messages.Message(p.LabelShortCap + " escaped. Their wounds, technique and memory of this encounter persist.", MessageTypeDefOf.NegativeEvent);
            return true;
        }
        public static void Tick(GameComponent_ColonyLife gc, int now)
        {
            foreach (var r in gc.rivals.Where(r => !r.ended).ToList())
            {
                Pawn p = r.pawn;
                if (p == null || p.Dead || p.Destroyed || p.Faction == Faction.OfPlayer || p.IsPrisoner || !p.HostileTo(Faction.OfPlayer))
                { r.ended = true; continue; }
                if (!p.Spawned)
                {
                    if (!gc.HeldRivals.Contains(p)) { r.ended = true; continue; }
                    if (now >= r.healAt)
                    {
                        foreach (var injury in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h => !h.IsPermanent()).ToList()) injury.Heal(8);
                        if (p.needs?.food != null) p.needs.food.CurLevelPercentage = 1;
                        if (p.needs?.rest != null) p.needs.rest.CurLevelPercentage = 1;
                        r.healAt = now + 60000;
                    }
                    if (now >= r.returnAt && now % 2500 == 0 && !p.Downed)
                    {
                        Map home = Find.Maps.Where(m => m.IsPlayerHome && m.mapPawns.FreeColonistsSpawned.Any()).RandomElementWithFallback();
                        if (home != null && !gc.rivals.Any(x => !x.ended && x.pawn?.Spawned == true)) Spawn(gc, home, r);
                    }
                    continue;
                }
                if (p.Downed || p.InMentalState) continue;
                if (p.health.summaryHealth.SummaryHealthPercent < 0.55f || now - r.arrivedAt > 60000)
                {
                    r.retreating = true;
                    if (p.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_RivalRetreat")
                        && CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(p.Map) && p.CanReach(c, PathEndMode.OnCell, Danger.Deadly), p.Map, CellFinder.EdgeRoadChance_Ignore, out IntVec3 exit))
                        p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_RivalRetreat"), exit));
                    continue;
                }
                if (now % 1000 != 0 || p.CurJob?.def == JobDefOf.AttackMelee || p.CurJob?.def == JobDefOf.AttackStatic) continue;
                Thing target = r.tactics == 2 ? p.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_CurtainAnchor"))
                    .Where(t => t.Faction == Faction.OfPlayer && p.CanReach(t, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(t => t.Position.DistanceToSquared(p.Position)).FirstOrDefault() : null;
                if (target == null && r.tactics == 0 && r.rememberedOpponent?.Spawned == true && r.rememberedOpponent.Map == p.Map
                    && !r.rememberedOpponent.Downed && r.rememberedOpponent.HostileTo(p)) target = r.rememberedOpponent;
                if (target == null) target = p.Map.mapPawns.FreeColonistsSpawned.Where(q => !q.Dead && !q.Downed && q.HostileTo(p))
                    .OrderBy(q => r.tactics == 1 ? q.health.summaryHealth.SummaryHealthPercent + (JJK_GradeUtility.IsSorcerer(q) ? 1 : 0)
                        : q.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                if (target == null) continue;
                var verb = p.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
                Job job = JobMaker.MakeJob(verb != null && !verb.IsMeleeAttack ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, target);
                job.expiryInterval = 1200; job.checkOverrideOnExpire = true; job.maxNumStaticAttacks = 3;
                p.jobs.TryTakeOrderedJob(job);
            }
            if (now < 1200000 || now < gc.nextRival || gc.rivals.Count >= 3 || gc.rivals.Any(r => !r.ended)) return;
            var homeMap = Find.Maps.Where(m => m.IsPlayerHome && m.mapPawns.FreeColonistsSpawned.Count(JJK_GradeUtility.IsSorcerer) >= 2).RandomElementWithFallback();
            gc.nextRival = now + 600000;
            if (homeMap != null && !Find.Storyteller.difficulty.peacefulTemples) Spawn(gc, homeMap);
        }
    }
    public class JobDriver_RivalRetreat : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Do(() =>
            {
                var gc = GameComponent_ColonyLife.Instance; var r = gc.rivals.FirstOrDefault(x => x.pawn == pawn);
                if (r != null) RivalWorld.Escape(gc, r);
            });
        }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PreApplyDamage))]
    public static class Patch_RivalMemory
    {
        public static void Postfix(Pawn_HealthTracker __instance, DamageInfo dinfo, bool absorbed)
        {
            if (absorbed || dinfo.Amount <= 0 || !(dinfo.Instigator is Pawn attacker)) return;
            var r = GameComponent_ColonyLife.Instance?.rivals.FirstOrDefault(x => !x.ended && x.pawn == __instance.pawn);
            if (r != null && attacker.Faction == Faction.OfPlayer) { r.rememberedOpponent = attacker; r.rememberedDamage = dinfo.Def; }
        }
    }
}
