using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using TaranMagicFramework;

namespace JJKMod
{
    public static class JJK_DebugCullingChecks
    {
        private static IntVec3 SpawnCell(Map map) => map.AllCells.First(c => c.Standable(map) && !c.GetThingList(map).Any(t => t is Pawn));
        private static Pawn Colonist(Map map, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle(name);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_IceFormation"), true);
            p.GetComp<CompAbilities>().RecheckAbilities();
            GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Special);
            GenSpawn.Spawn(p, SpawnCell(map), map);
            return p;
        }
        private static Caravan Form(Pawn p, PlanetTile tile)
        { if (p.Spawned) p.DeSpawn(); return CaravanMaker.MakeCaravan(new[] { p }, Faction.OfPlayer, tile, true); }
        private static void Enter(Pawn p, CullingSite site)
        {
            var caravan = Form(p, site.Tile);
            GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, new IntVec3(100, 1, 100), null);
            new CaravanArrivalAction_VisitSite(site).Arrived(caravan);
            GameComponent_CullingGame.Instance.TrackVisitors(site);
        }
        private static void KillWave(CullingSite site, Pawn killer)
        {
            foreach (var enemy in site.rivals.Where(p => p != null && !p.Dead && !p.Destroyed).ToList())
                enemy.Kill(new DamageInfo(DamageDefOf.Cut, 9999, instigator: killer));
        }
        private static void Clear(CullingSite site, Pawn p)
        {
            while (!site.Secured)
            {
                KillWave(site, p);
                if (site.wave >= site.WaveLimit) break;
                site.nextWaveTick = Math.Max(1, Find.TickManager.TicksGame);
                site.UpdateEncounter(site.nextWaveTick);
            }
        }
        [DebugAction("Rim-Kaisen", "Run Culling Game checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void QueueRun() => LongEventHandler.QueueLongEvent(Run, "GeneratingMapForNewEncounter", false, null);
        private static void Run()
        {
            var gc = GameComponent_CullingGame.Instance; var original = gc.data; Map home = Find.CurrentMap;
            gc.data = new CullingData(); var made = new List<Pawn>(); int pass = 0, fail = 0;
            Action<bool, string> check = (ok, name) => { if (ok) pass++; else fail++; Log.Message("[JJK Culling QA] " + (ok ? "PASS " : "FAIL ") + name); };
            try
            {
                check(gc.Begin(home, true), "campaign begins with existing hostile faction");
                check(gc.data.sites.Count == 3 && gc.data.sites.Select(s => s.Tile).Distinct().Count() == 3
                    && gc.data.sites.All(s => !s.HasMap), "three distinct unloaded barrier destinations");
                check(!gc.Begin(home, true), "duplicate campaign cannot create more sites");
                var p = Colonist(home, "Culling Adept QA"); var ally = Colonist(home, "Culling Partner QA"); made.Add(p); made.Add(ally);
                var a = gc.Register(p); var b = gc.Register(ally);
                check(gc.Register(p) == a && gc.data.players.Count == 2, "participant enrollment is idempotent");
                check(!gc.Transfer(p, ally, 1) && !gc.BuyRule(p, CullingRule.FreePassage), "locked transfers and unaffordable rule blocked");
                var first = gc.data.sites[0];
                Enter(p, first);
                check(p.Map == first.Map && first.generated && first.wave == 1 && first.Remaining == 5, "engine arrival generates first wave and anchor");
                check(first.rivals.All(r => JJK_GradeUtility.IsSorcerer(r) && JJK_Utils.HasCursedTechnique(r)), "authored rival kits have reserves and techniques");
                first.Generate(first.Map); check(first.rivals.Count == 5 && first.wave == 1, "map generation cannot duplicate defenders");
                check(!gc.Disrupt(first, p) && !gc.Claim(p), "uncleared anchor and premature reward blocked");
                var enemy = first.rivals[0]; enemy.Kill(new DamageInfo(DamageDefOf.Cut, 9999, instigator: p));
                check(a.points == 5, "real Pawn.Kill hook awards five points for registered sorcerer");
                gc.OnKill(enemy, p); check(a.points == 5, "same death cannot award twice");
                var unregistered = PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, GameComponent_CullingGame.RivalFaction);
                GenSpawn.Spawn(unregistered, SpawnCell(first.Map), first.Map); made.Add(unregistered);
                unregistered.Kill(new DamageInfo(DamageDefOf.Cut, 9999, instigator: p));
                check(a.points == 5, "unregistered opponents award no farmable points");
                var environmental = first.rivals[1]; environmental.Kill(null); gc.OnKill(environmental, p);
                check(a.points == 5 && gc.Player(environmental).credited, "environmental death cannot be reclaimed later");
                a.lastSite = first; gc.Depart(a);
                check(a.points == 0 && gc.data.breaches == 1, "retreat forfeits score and increases pressure");
                gc.Depart(a); check(gc.data.breaches == 1, "departure penalty applied once per visit");
                gc.TrackVisitors(first); check(a.lastSite == first && !first.ShouldRemoveMapNow(out _), "active encounter persists and visitor tracked");
                a.points = 100; a.lastScoreTick = 123;
                check(gc.BuyRule(p, CullingRule.PointTransfer) && a.points == 0 && a.lastScoreTick == 123, "rule costs exactly 100 without resetting inactivity");
                a.points = 100; check(!gc.BuyRule(p, CullingRule.PointTransfer) && a.points == 100, "rule cannot be purchased twice");
                check(gc.BuyRule(p, CullingRule.FreePassage) && gc.data.freePassage, "free passage purchased");
                a.points = 10; a.lastSite = first; gc.Depart(a);
                check(a.points == 10 && gc.data.breaches == 1, "free passage preserves score and pressure");
                check(!gc.Transfer(p, p, 1) && !gc.Transfer(p, ally, 0) && !gc.Transfer(p, ally, -1)
                    && !gc.Transfer(p, ally, 11), "self zero negative and overdraw transfers blocked");
                check(gc.Transfer(p, ally, 3) && a.points == 7 && b.points == 3, "transfer conserves score");
                check(a.lastScoreTick == Find.TickManager.TicksGame && b.lastScoreTick == a.lastScoreTick, "transfer resets both inactivity clocks");
                a.lastScoreTick = 0;
                gc.CheckInactivity(a, GameComponent_CullingGame.InactivityTicks - 1);
                check(!TechniqueSealUtil.IsSealed(p), "no early inactivity seal");
                gc.CheckInactivity(a, GameComponent_CullingGame.InactivityTicks);
                check(TechniqueSealUtil.IsSealed(p) && !p.Dead, "nineteen-day penalty is a real nonlethal technique seal");
                gc.CheckInactivity(a, GameComponent_CullingGame.InactivityTicks + 60);
                check(p.health.hediffSet.hediffs.Count(h => h.def.defName == "JJK_CullingStagnation") == 1, "penalty does not stack each update");
                check(gc.Transfer(ally, p, 1) && !TechniqueSealUtil.IsSealed(p), "legal transfer removes inactivity seal");
                foreach (var site in gc.data.sites.ToList())
                {
                    if (p.Map != site.Map) Enter(p, site);
                    int oldWave = site.wave;
                    KillWave(site, p); site.UpdateEncounter(Find.TickManager.TicksGame);
                    check(site.wave == oldWave && site.nextWaveTick > Find.TickManager.TicksGame, "next wave has recovery delay at colony " + site.number);
                    Clear(site, p);
                    check(site.wave == 4 && site.rivals.Count == 20 && site.Secured, "colony " + site.number + " has four finite waves");
                    p.Position = CellFinder.RandomClosewalkCellNear(site.anchor.Position, site.Map, 1);
                    check(gc.Disrupt(site, p) && !gc.Disrupt(site, p), "anchor " + site.number + " disrupted exactly once");
                }
                var final = gc.data.sites.Single(s => s.finale);
                check(gc.Anchors == 3 && gc.data.sites.Count == 4 && !final.HasMap, "three anchors reveal one final ritual site");
                gc.EnsureFinale(); check(gc.data.sites.Count == 4, "finale is not duplicated");
                Enter(p, final); Clear(final, p);
                check(final.wave == 3 && final.rivals.Count == 15 && final.Secured, "final ritual has three finite waves");
                p.Position = CellFinder.RandomClosewalkCellNear(final.anchor.Position, final.Map, 1);
                check(gc.Disrupt(final, p) && gc.data.phase == CullingPhase.Victory, "final disruption ends campaign");
                check(!gc.Claim(p), "reward requires return to original home");
                check(!final.ShouldRemoveMapNow(out _), "terminal map still retains its player colonist");
                var returnCaravan = Form(p, home.Tile); final.Notify_CaravanFormed(returnCaravan);
                new CaravanArrivalAction_Enter(home.Parent).Arrived(returnCaravan);
                check(gc.Claim(p) && GameComponent_JJK.Instance.GetGrade(p) == SorcererGrade.Calamity, "returned special-grade participant can claim Calamity");
                check(!gc.Claim(ally) && !gc.Claim(p), "campaign reward can only be claimed once");
                check(gc.data.sites.All(s => s.ShouldRemoveMapNow(out _)), "all terminal empty sites allow cleanup");
                check(!gc.Begin(home, true), "finished campaign cannot restart");
                gc.data.phase = CullingPhase.Active; a.lastScoreTick = 0;
                gc.CheckInactivity(a, GameComponent_CullingGame.InactivityTicks); gc.Abandon();
                check(!TechniqueSealUtil.IsSealed(p) && gc.data.phase == CullingPhase.Abandoned && !gc.Claim(p), "abandonment clears penalty and prevents reward");
            }
            catch (Exception ex) { fail++; Log.Error("[JJK Culling QA] " + ex); }
            finally
            {
                var sites = gc.data.sites.ToList(); gc.data.phase = CullingPhase.Abandoned;
                foreach (var p in made) if (!p.Destroyed) p.Destroy();
                gc.data = original; Current.Game.CurrentMap = home;
                foreach (var site in sites) LongEventHandler.ExecuteWhenFinished(() => {
                    if (site != null && !site.Destroyed) { if (site.HasMap) site.CheckRemoveMapNow(); else site.Destroy(); }
                });
                Log.Message($"[JJK Culling QA] RESULT {pass} passed, {fail} failed");
            }
        }
        [DebugAction("Rim-Kaisen", "Prepare Culling Game travel QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            var gc = GameComponent_CullingGame.Instance; Map home = Find.CurrentMap;
            if (!gc.Begin(home, true)) return;
            var p = Colonist(home, "Culling Adept QA");
            var food = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack); food.stackCount = 40; p.inventory.innerContainer.TryAdd(food);
            var caravan = Form(p, home.Tile);
            caravan.pather.StartPath(gc.data.sites[0].Tile, new CaravanArrivalAction_VisitSite(gc.data.sites[0]));
            Report();
        }
        [DebugAction("Rim-Kaisen", "Clear Culling Game waves QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearCurrent()
        {
            var site = Find.CurrentMap.Parent as CullingSite;
            var p = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.Name?.ToStringFull == "Culling Adept QA");
            if (site == null || p == null) return;
            Clear(site, p); Report();
        }
        [DebugAction("Rim-Kaisen", "Report Culling Game QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        public static void Report()
        {
            var gc = GameComponent_CullingGame.Instance; var d = gc.data;
            Log.Message($"[JJK Culling QA] STATE phase={d.phase} anchors={gc.Anchors} transfer={d.pointTransfer} passage={d.freePassage} reward={d.rewardClaimed} raids={d.raids} breaches={d.breaches}");
            foreach (var s in d.sites.Where(s => s != null)) Log.Message($"[JJK Culling QA] SITE id={s.ID} tile={s.Tile} generated={s.generated} wave={s.wave} rivals={s.Remaining} disrupted={s.disrupted} anchor={s.anchor?.ThingID} pos={s.anchor?.Position}");
            foreach (var p in d.players.Where(p => GameComponent_CullingGame.LivingPlayer(p.pawn)))
                Log.Message($"[JJK Culling QA] PLAYER {p.pawn.ThingID} {p.name} points={p.points} lastScore={p.lastScoreTick} lastSite={p.lastSite?.ID}");
        }
    }
}
