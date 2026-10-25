using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace JJKMod
{
    public enum CullingPhase { Dormant, Offered, Active, Victory, Abandoned }
    public enum CullingRule { PointTransfer, FreePassage, NegotiatedTruce, Bounties, ProtectedAreas }

    public class CullingPlayer : IExposable
    {
        public Pawn pawn;
        public string name;
        public int points, lastScoreTick;
        public int bounty;
        public bool sorcerer, credited, warned;
        public CullingSite lastSite;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn"); Scribe_References.Look(ref lastSite, "lastSite");
            Scribe_Values.Look(ref name, "name"); Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref bounty, "bounty");
            Scribe_Values.Look(ref lastScoreTick, "lastScoreTick"); Scribe_Values.Look(ref sorcerer, "sorcerer");
            Scribe_Values.Look(ref credited, "credited"); Scribe_Values.Look(ref warned, "warned");
        }
    }

    // One finite campaign per save. World sites own their encounters; this ledger owns rules and scores.
    public class CullingData : IExposable
    {
        public CullingPhase phase;
        public Map home;
        public List<CullingSite> sites = new List<CullingSite>();
        public List<CullingPlayer> players = new List<CullingPlayer>();
        public bool pointTransfer, freePassage, rewardClaimed;
        public bool negotiatedTruce, bounties, protectedAreas;
        public int rivalPressure;
        public int nextRaidTick, raids, breaches, anchorsBroken;
        public void ExposeData()
        {
            Scribe_Values.Look(ref phase, "phase"); Scribe_References.Look(ref home, "home");
            Scribe_Collections.Look(ref sites, "sites", LookMode.Reference);
            Scribe_Collections.Look(ref players, "players", LookMode.Deep);
            Scribe_Values.Look(ref pointTransfer, "pointTransfer"); Scribe_Values.Look(ref freePassage, "freePassage");
            Scribe_Values.Look(ref negotiatedTruce, "negotiatedTruce"); Scribe_Values.Look(ref bounties, "bounties");
            Scribe_Values.Look(ref protectedAreas, "protectedAreas"); Scribe_Values.Look(ref rivalPressure, "rivalPressure");
            Scribe_Values.Look(ref rewardClaimed, "rewardClaimed"); Scribe_Values.Look(ref nextRaidTick, "nextRaidTick");
            Scribe_Values.Look(ref raids, "raids"); Scribe_Values.Look(ref breaches, "breaches");
            Scribe_Values.Look(ref anchorsBroken, "anchorsBroken");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { sites = sites ?? new List<CullingSite>(); players = players ?? new List<CullingPlayer>(); }
        }
    }

    public class GameComponent_CullingGame : GameComponent
    {
        public const int InactivityTicks = 19 * 60000;
        public CullingData data = new CullingData();
        public static GameComponent_CullingGame Instance => Current.Game?.GetComponent<GameComponent_CullingGame>();
        public GameComponent_CullingGame(Game game) { }
        public bool Active => data.phase == CullingPhase.Active;
        public int Anchors => data.anchorsBroken;
        public CullingPlayer Player(Pawn pawn) => pawn == null ? null : data.players.FirstOrDefault(p => p.pawn == pawn);
        public static bool LivingPlayer(Pawn p) => p != null && !p.Dead && !p.Destroyed && p.IsColonist && p.Faction == Faction.OfPlayer;
        public static Faction RivalFaction => Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
            f.def.defName == "JJK_CurseUsers" && !f.defeated && f.HostileTo(Faction.OfPlayer));
        public bool Eligible(Map map) => map != null && map.IsPlayerHome && RivalFaction != null
            && GenDate.DaysPassed >= 60 && map.mapPawns.FreeColonistsSpawned.Any(p =>
                JJK_GradeUtility.IsSorcerer(p) && GameComponent_JJK.Instance.GetGrade(p) >= SorcererGrade.Special);

        public CullingPlayer Register(Pawn pawn)
        {
            if (!Active || pawn == null || pawn.Dead || pawn.Destroyed) return null;
            var record = Player(pawn);
            if (record != null) return record;
            record = new CullingPlayer { pawn = pawn, name = pawn.LabelShortCap, lastScoreTick = Find.TickManager.TicksGame,
                sorcerer = JJK_GradeUtility.IsSorcerer(pawn) || JJK_Utils.IsCursedSpirit(pawn) };
            data.players.Add(record);
            return record;
        }

        public bool Begin(Map map, bool dev = false)
        {
            if ((data.phase != CullingPhase.Dormant && data.phase != CullingPhase.Offered) || map == null
                || !map.IsPlayerHome || RivalFaction == null || (!dev && !Eligible(map))) return false;
            // Reserve all three distinct destinations before committing the campaign.
            var made = new List<CullingSite>();
            for (int i = 0; i < 3; i++)
            {
                var site = CreateSite(map, i + 1, false);
                if (site == null)
                {
                    foreach (var s in made) s.Destroy();
                    Messages.Message("JJK_CullingNoTile".Translate(), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                made.Add(site);
            }
            data.home = map; data.sites = made; data.phase = CullingPhase.Active;
            data.nextRaidTick = Find.TickManager.TicksGame + 300000;
            Letter("JJK_CullingStarted", made[0]);
            return true;
        }

        private CullingSite CreateSite(Map home, int number, bool finale)
        {
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, home.Tile, 2, 12, selectLandmarkChance: 0f)) return null;
            var site = (CullingSite)SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("JJK_CullingColonyPart"),
                tile, null, false, 0, DefDatabase<WorldObjectDef>.GetNamed("JJK_CullingColony"));
            site.number = number; site.finale = finale; site.sitePartsKnown = true;
            site.customLabel = finale ? "JJK_CullingRitual".Translate().ToString() : "JJK_CullingColony".Translate(number).ToString();
            Find.WorldObjects.Add(site);
            return site;
        }

        public static Pawn KillOwner(Pawn killer) => killer?.health?.hediffSet?.hediffs.OfType<Hediff_ShikigamiBond>().FirstOrDefault()?.owner ?? killer;
        public void OnKill(Pawn victim, Pawn killer, bool wasHostile = false)
        {
            if (!Active || victim == null || !victim.Dead) return;
            if (victim.MapHeld?.Parent is CullingSite site) TrackVisitors(site);
            var killed = Player(victim);
            if (killed == null || killed.credited) return;
            killed.credited = true; // Even environmental deaths cannot later be claimed by replaying the hook.
            var winner = Player(killer);
            if (winner == null || killer == victim || killer.Dead || killer.Destroyed || !wasHostile
                || (killer.Faction == Faction.OfPlayer && victim.Faction == Faction.OfPlayer)) return;
            winner.points += killed.sorcerer ? 5 : 1;
            if (killed.bounty > 0 && LivingPlayer(killer))
            {
                winner.points += killed.bounty + 10; killed.bounty = 0;
                GameComponent_CampaignHistory.Instance?.Add(killer, "JJK_RecordBounty", killed.name);
            }
            ScoreChanged(winner);
        }

        private void ScoreChanged(CullingPlayer player)
        {
            player.lastScoreTick = Find.TickManager.TicksGame; player.warned = false;
            RemovePenalty(player.pawn);
        }
        private static HediffDef PenaltyDef => DefDatabase<HediffDef>.GetNamed("JJK_CullingStagnation");
        private static void RemovePenalty(Pawn p)
        {
            if (p?.health == null) return;
            foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def == PenaltyDef).ToList()) p.health.RemoveHediff(h);
        }
        public bool BuyRule(Pawn payer, CullingRule rule)
        {
            var p = Player(payer);
            if (!Active || !LivingPlayer(payer) || p == null || p.points < 100
                || !Enum.IsDefined(typeof(CullingRule), rule) || RuleEnabled(rule)) return false;
            p.points -= 100; // Rule purchases do not reset the inactivity clock.
            switch (rule)
            {
                case CullingRule.PointTransfer: data.pointTransfer = true; break;
                case CullingRule.FreePassage: data.freePassage = true; break;
                case CullingRule.NegotiatedTruce: data.negotiatedTruce = true; break;
                case CullingRule.Bounties: data.bounties = true; break;
                case CullingRule.ProtectedAreas: data.protectedAreas = true; data.rivalPressure = Math.Min(3, data.rivalPressure + 1); break;
            }
            Messages.Message("JJK_CullingRuleAdded".Translate(("JJK_CullingRule" + rule).Translate()), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }
        public bool RuleEnabled(CullingRule rule) => rule == CullingRule.PointTransfer ? data.pointTransfer
            : rule == CullingRule.FreePassage ? data.freePassage : rule == CullingRule.NegotiatedTruce ? data.negotiatedTruce
            : rule == CullingRule.Bounties ? data.bounties : rule == CullingRule.ProtectedAreas && data.protectedAreas;
        public bool PostBounty(Pawn payer, Pawn target)
        {
            var p = Player(payer); var t = Player(target);
            if (!Active || !data.bounties || !LivingPlayer(payer) || p == null || p.points < 20 || t == null || t.bounty > 0
                || target.Dead || target.Destroyed || !target.HostileTo(Faction.OfPlayer)) return false;
            p.points -= 20; t.bounty = 20; data.rivalPressure = Math.Min(3, data.rivalPressure + 1);
            Messages.Message("JJK_BountyReaction".Translate(t.name), MessageTypeDefOf.NegativeEvent);
            return true;
        }
        public bool Negotiate(Pawn payer, CullingSite site)
        {
            var p = Player(payer);
            if (!Active || !data.negotiatedTruce || !LivingPlayer(payer) || p == null || p.points < 20
                || site == null || !data.sites.Contains(site) || site.finale || !site.generated || site.truceUsed || site.disrupted
                || site.Remaining != 0 || site.wave >= site.WaveLimit || payer.Map != site.Map
                || data.players.Any(r => r.bounty > 0 && r.pawn != null && !r.pawn.Dead && r.pawn.MapHeld == site.Map)) return false;
            p.points -= 20; site.truceUsed = true; site.wave++; site.nextWaveTick = Find.TickManager.TicksGame + 60000;
            data.nextRaidTick = Math.Max(data.nextRaidTick, Find.TickManager.TicksGame + 60000);
            GameComponent_CampaignHistory.Instance?.Add(payer, "JJK_RecordTruce", site.LabelCap);
            Messages.Message("JJK_TruceReaction".Translate(), MessageTypeDefOf.PositiveEvent);
            return true;
        }
        public bool Transfer(Pawn from, Pawn to, int amount)
        {
            var source = Player(from); var dest = Player(to);
            if (!Active || !data.pointTransfer || !LivingPlayer(from) || !LivingPlayer(to) || from == to
                || source == null || dest == null || amount <= 0 || amount > source.points || dest.points > int.MaxValue - amount) return false;
            source.points -= amount; dest.points += amount; ScoreChanged(source); ScoreChanged(dest); return true;
        }
        public void Depart(CullingPlayer record)
        {
            var old = record.lastSite; record.lastSite = null;
            if (!Active || old == null || old.disrupted || data.freePassage || !LivingPlayer(record.pawn)) return;
            record.points = 0; data.breaches = Math.Min(4, data.breaches + 1);
            Messages.Message("JJK_CullingRetreat".Translate(record.name), record.pawn, MessageTypeDefOf.NegativeEvent, false);
        }
        public void TrackVisitors(CullingSite site)
        {
            if (!Active || site == null || !site.HasMap) return;
            foreach (var pawn in site.Map.mapPawns.FreeColonistsSpawned)
            {
                var record = Register(pawn);
                if (record == null) continue;
                if (record.lastSite != null && record.lastSite != site) Depart(record);
                record.lastSite = site;
            }
        }
        public void CheckInactivity(CullingPlayer player, int now)
        {
            if (!Active || !LivingPlayer(player.pawn) || !JJK_Utils.HasCursedTechnique(player.pawn)) return;
            if (now - player.lastScoreTick >= InactivityTicks)
            {
                if (!player.pawn.health.hediffSet.HasHediff(PenaltyDef))
                { player.pawn.health.AddHediff(PenaltyDef); Messages.Message("JJK_CullingSealed".Translate(player.name), player.pawn, MessageTypeDefOf.NegativeEvent, false); }
            }
            else if (!player.warned && now - player.lastScoreTick >= InactivityTicks - 60000)
            { player.warned = true; Letter("JJK_CullingWarning", player.pawn); }
        }

        public bool Disrupt(CullingSite site, Pawn pawn)
        {
            if (!Active || site == null || !data.sites.Contains(site) || site.disrupted || !site.Secured
                || !LivingPlayer(pawn) || pawn.Downed || pawn.Map != site.Map || site.anchor == null || site.anchor.Destroyed
                || !pawn.Position.InHorDistOf(site.anchor.Position, 3f)) return false;
            TrackVisitors(site); site.disrupted = true;
            if (site.finale)
            {
                data.phase = CullingPhase.Victory;
                foreach (var participant in data.players.Where(p => LivingPlayer(p.pawn)))
                    GameComponent_CampaignHistory.Instance?.Add(participant.pawn, "JJK_RecordVictory", "JJK_CullingRitual".Translate());
                foreach (var p in data.players) RemovePenalty(p.pawn);
                Letter("JJK_CullingVictory", pawn);
            }
            else { data.anchorsBroken++; Letter("JJK_CullingAnchorBroken", site); EnsureFinale(); }
            return true;
        }
        public void EnsureFinale()
        {
            if (!Active || Anchors < 3 || data.home == null || data.sites.Any(s => s != null && s.finale)) return;
            var site = CreateSite(data.home, 4, true);
            if (site == null) return; // Retry at the next campaign update; never lose a completed anchor.
            data.sites.Add(site); Letter("JJK_CullingFinale", site);
        }
        public bool Claim(Pawn pawn)
        {
            if (data.phase != CullingPhase.Victory || data.rewardClaimed || !LivingPlayer(pawn) || Player(pawn) == null
                || data.home == null || !pawn.Spawned || pawn.Map != data.home || !JJK_GradeUtility.IsSorcerer(pawn)
                || GameComponent_JJK.Instance.GetGrade(pawn) != SorcererGrade.Special) return false;
            data.rewardClaimed = true; GameComponent_JJK.Instance.UnlockCalamity(pawn);
            Messages.Message("JJK_CullingReward".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }
        public void Abandon()
        {
            if (!Active) return;
            data.phase = CullingPhase.Abandoned;
            foreach (var p in data.players) RemovePenalty(p.pawn);
        }
        private static void Letter(string key, LookTargets target) => Find.LetterStack.ReceiveLetter(
            "JJK_Kogane".Translate(), key.Translate(), LetterDefOf.NeutralEvent, target);

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            if (data.phase == CullingPhase.Dormant && now % 2500 == 0 && Find.Storyteller.def.defName == "JJK_Kenjaku")
            {
                var home = Find.Maps.FirstOrDefault(Eligible);
                if (home != null) { data.phase = CullingPhase.Offered; Letter("JJK_CullingOffer", home.Parent); }
            }
            if (Active)
            {
                if (data.home == null || !Find.Maps.Contains(data.home) || RivalFaction == null) Abandon();
                else
                {
                    foreach (var site in data.sites.Where(s => s != null && !s.Destroyed).ToList())
                    { TrackVisitors(site); site.UpdateEncounter(now); }
                    foreach (var p in data.players.ToList())
                    {
                        if (p.lastSite != null && p.pawn?.MapHeld != p.lastSite.Map) Depart(p);
                        CheckInactivity(p, now);
                    }
                    EnsureFinale();
                    if (now >= data.nextRaidTick && data.raids < 3 && RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, data.home, CellFinder.EdgeRoadChance_Animal))
                    {
                        CullingSite.SpawnRivals(data.home, edge, 2 + data.raids + Math.Min(2, data.breaches) + data.rivalPressure, false, false);
                        data.raids++; data.nextRaidTick = now + 300000;
                        Find.LetterStack.ReceiveLetter("JJK_Kogane".Translate(), "JJK_CullingRaid".Translate(data.raids), LetterDefOf.ThreatBig, new TargetInfo(edge, data.home));
                    }
                }
            }
            if (!Active && now % 300 == 0)
                foreach (var site in data.sites.Where(s => s != null && !s.Destroyed).ToList())
                    if (site.HasMap) site.CheckRemoveMapNow(); else site.Destroy();
        }
        public override void ExposeData()
        { Scribe_Deep.Look(ref data, "cullingGame"); if (data == null) data = new CullingData(); }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CullingDeath
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out Pawn __state)
        {
            __state = null;
            var gc = GameComponent_CullingGame.Instance;
            if (gc?.Active != true || __instance.Dead) return;
            if (__instance.MapHeld?.Parent is CullingSite site) gc.TrackVisitors(site);
            Pawn killer = GameComponent_CullingGame.KillOwner(dinfo?.Instigator as Pawn);
            if (killer != null && __instance.HostileTo(killer)) __state = killer;
        }
        public static void Postfix(Pawn __instance, Pawn __state) => GameComponent_CullingGame.Instance?.OnKill(__instance, __state, __state != null);
    }
}
