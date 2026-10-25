using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    public static class JJK_DebugExpeditionChecks
    {
        private static Pawn Colonist(Map home)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle("Expedition Courier QA");
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.GetComp<CompAbilities>().RecheckAbilities();
            GenSpawn.Spawn(p, JJK_DebugAdvancedPuppetChecks.Clear(home), home);
            return p;
        }
        private static Caravan Form(IEnumerable<Pawn> party, PlanetTile tile)
        {
            var list = party.ToList();
            foreach (var p in list) if (p.Spawned) p.DeSpawn();
            return CaravanMaker.MakeCaravan(list, Faction.OfPlayer, tile, true);
        }
        [DebugAction("Rim-Kaisen", "Run expedition checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void QueueRun() => LongEventHandler.QueueLongEvent(Run, "GeneratingMapForNewEncounter", false, null);
        private static void Run()
        {
            Map home = Find.CurrentMap;
            var gc = GameComponent_Jujutsu.Instance;
            Faction school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
            var standing = gc.Standing(school); int oldRep = standing.reputation, oldOffer = standing.nextOfferTick;
            var created = new List<Pawn>(); var missions = new List<JujutsuContract>();
            int pass = 0, fail = 0;
            Action<bool, string> check = (ok, name) => { if (ok) pass++; else fail++; Log.Message("[JJK Expedition QA] " + (ok ? "PASS " : "FAIL ") + name); };
            try
            {
                foreach (ContractKind kind in new[] { ContractKind.Infestation, ContractKind.Rescue, ContractKind.Recovery })
                {
                    standing.nextOfferTick = 0;
                    var c = ExorcismExpeditions.Accept(school, home, 1, kind); missions.Add(c);
                    check(c != null && c.site.Spawned && !c.site.HasMap && c.map == home, kind + " creates separate world destination without loading map");
                    if (c == null) continue;
                    gc.Evaluate(c); check(c.state == ContractState.Active && !gc.Claim(c), kind + " unvisited site cannot pay or fail for ungenerated subject");
                    check(ExorcismExpeditions.Accept(school, home, 1, kind) == null, kind + " shares one-contract school cap");
                    var p = Colonist(home); created.Add(p);
                    var caravan = Form(new[] { p }, home.Tile);
                    check(caravan.pather.StartPath(c.site.Tile, new CaravanArrivalAction_VisitSite(c.site)), kind + " caravan path to site is reachable");
                    caravan.pather.StopDead(); caravan.Tile = c.site.Tile;
                    // Generate via the engine then use the normal arrival action (no simulated map).
                    GetOrGenerateMapUtility.GetOrGenerateMap(c.site.Tile, new IntVec3(100, 1, 100), null);
                    new CaravanArrivalAction_VisitSite(c.site).Arrived(caravan);
                    ExorcismExpeditions.TrackParty(c);
                    check(p.Spawned && p.Map == c.site.Map && c.siteGenerated && c.threats.Count > 0, kind + " engine arrival generates encounter and enters caravan");
                    check(c.participants.Contains(p) && !c.ObjectiveMet(), kind + " records visitor and blocks uncleared objective");
                    int count = c.threats.Count; var subject = c.artifact ?? (Thing)c.rescue;
                    c.site.parts[0].def.Worker.PostMapGenerate(c.site.Map);
                    check(c.threats.Count == count && (c.artifact ?? (Thing)c.rescue) == subject, kind + " encounter generation is one-shot");
                    var retreat = Form(new[] { p }, c.site.Tile); c.site.Notify_CaravanFormed(retreat);
                    check(!c.site.ShouldRemoveMapNow(out _) && c.site.HasMap, kind + " retreat preserves the encounter");
                    new CaravanArrivalAction_VisitSite(c.site).Arrived(retreat);
                    if (kind == ContractKind.Rescue)
                        check(c.rescue.ParentHolder is Building_SealedRefuge && !ExorcismExpeditions.Recruit(c), "survivor protected in refuge and cannot be released before exorcism");
                    foreach (var enemy in c.threats.ToList()) if (!enemy.Dead && !enemy.Destroyed) enemy.Kill(null);
                    gc.Evaluate(c);
                    check(c.siteCleared && !gc.Claim(c), kind + " cleared site still requires return home");
                    var party = new List<Pawn> { p };
                    if (kind == ContractKind.Rescue)
                    {
                        check(ExorcismExpeditions.Recruit(c) && c.rescue.Spawned && c.rescue.Faction == Faction.OfPlayer, "release makes living survivor caravan-controllable");
                        check(!ExorcismExpeditions.Recruit(c), "survivor cannot be recruited twice");
                        party.Add(c.rescue); created.Add(c.rescue);
                    }
                    if (kind == ContractKind.Recovery)
                    {
                        check(c.artifact.stackCount == 1 && c.artifact.def.tradeability == Tradeability.None, "objective item unique and untradeable");
                        var wrong = ThingMaker.MakeThing(c.artifact.def); GenPlace.TryPlaceThing(wrong, home.Center, home, ThingPlaceMode.Near);
                        check(!c.ObjectiveMet(), "substitute object at home does not satisfy recovery"); wrong.Destroy();
                        c.artifact.DeSpawn(); check(p.inventory.innerContainer.TryAdd(c.artifact), "specific mission object can be carried in caravan inventory");
                    }
                    var returning = Form(party, c.site.Tile); c.site.Notify_CaravanFormed(returning);
                    check(returning.pather.StartPath(home.Tile, new CaravanArrivalAction_Enter(home.Parent)), kind + " return route is valid");
                    returning.pather.StopDead(); returning.Tile = home.Tile;
                    new CaravanArrivalAction_Enter(home.Parent).Arrived(returning);
                    gc.Evaluate(c); check(c.state == ContractState.Ready && c.ObjectiveMet(), kind + " arriving home completes objective");
                    int rep = standing.reputation;
                    check(gc.Claim(c) && standing.reputation == Math.Min(100, rep + 10), kind + " pays existing reward and reputation");
                    check(!gc.Claim(c) && standing.reputation == Math.Min(100, rep + 10), kind + " duplicate claim rejected");
                    if (kind == ContractKind.Recovery) check(c.artifact.Destroyed, "claim hands over the actual sealed object");
                    foreach (var member in party) if (!member.Destroyed) member.Destroy();
                    check(c.site.ShouldRemoveMapNow(out bool remove) && remove, kind + " completed empty site permits removal");
                }
                standing.nextOfferTick = 0;
                var expired = ExorcismExpeditions.Accept(school, home, 1, ContractKind.Recovery); missions.Add(expired);
                expired.expires = Find.TickManager.TicksGame; int before = standing.reputation; gc.Evaluate(expired); gc.Evaluate(expired);
                check(expired.state == ContractState.Failed && standing.reputation == Math.Max(0, before - 5), "deadline fails unvisited mission exactly once");
            }
            catch (Exception e) { fail++; Log.Error("[JJK Expedition QA] " + e); }
            finally
            {
                foreach (var p in created) if (!p.Destroyed) p.Destroy();
                foreach (var c in missions.Where(c => c != null))
                {
                    c.state = ContractState.Failed;
                    var site = c.site;
                    // MapGenerator queues graphics initialization for the end of the long event.
                    // Do not dispose a freshly generated map before that initialization runs.
                    LongEventHandler.ExecuteWhenFinished(() => {
                        if (site != null && !site.Destroyed) { if (site.HasMap) site.CheckRemoveMapNow(); else site.Destroy(); }
                    });
                    gc.contracts.Remove(c);
                }
                standing.reputation = oldRep; standing.nextOfferTick = oldOffer;
                Current.Game.CurrentMap = home;
                Log.Message($"[JJK Expedition QA] RESULT {pass} passed, {fail} failed");
            }
        }
        [DebugAction("Rim-Kaisen", "Prepare expedition travel QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            Map home = Find.CurrentMap;
            var school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
            GameComponent_Jujutsu.Instance.Standing(school).nextOfferTick = 0;
            var c = ExorcismExpeditions.Accept(school, home, 1, ContractKind.Recovery);
            if (c == null) return;
            var p = Colonist(home);
            var food = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack); food.stackCount = 30; p.inventory.innerContainer.TryAdd(food);
            var caravan = Form(new[] { p }, home.Tile);
            caravan.pather.StartPath(c.site.Tile, new CaravanArrivalAction_VisitSite(c.site));
            Log.Message($"[JJK Expedition QA] TRAVEL pawn={p.ThingID} caravan={caravan.ID} from={home.Tile} to={c.site.Tile} home={home.uniqueID}");
        }
        [DebugAction("Rim-Kaisen", "Report expedition QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void Report()
        {
            foreach (var c in GameComponent_Jujutsu.Instance.contracts.Where(c => c.expedition))
                Log.Message($"[JJK Expedition QA] STATE {c.kind} {c.state} site={c.site?.ID} tile={c.site?.Tile} generated={c.siteGenerated} cleared={c.siteCleared} remaining={c.Remaining} artifact={c.artifact?.ThingID} holder={c.artifact?.ParentHolder} survivor={c.rescue?.ThingID} party={string.Join(",",c.participants.Select(p=>p.ThingID))} expires={c.expires}");
            foreach (var c in Find.WorldObjects.Caravans.Where(c => c.PawnsListForReading.Any(p => p.Name?.ToStringFull == "Expedition Courier QA")))
                Log.Message($"[JJK Expedition QA] CARAVAN {c.ID} tile={c.Tile} moving={c.pather.Moving} next={c.pather.nextTile} dest={c.pather.Destination}");
        }
        [DebugAction("Rim-Kaisen", "Secure expedition and depart QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Depart()
        {
            var c = GameComponent_Jujutsu.Instance.contracts.First(c => c.expedition && c.kind == ContractKind.Recovery && c.siteGenerated && c.state == ContractState.Active);
            var p = c.site.Map.mapPawns.FreeColonistsSpawned.First(p => p.Name?.ToStringFull == "Expedition Courier QA");
            ExorcismExpeditions.TrackParty(c);
            foreach (var threat in c.threats.ToList()) if (!threat.Dead && !threat.Destroyed) threat.Kill(null);
            ExorcismExpeditions.Evaluate(c);
            c.artifact.DeSpawn(); p.inventory.innerContainer.TryAdd(c.artifact);
            var caravan = Form(new[] { p }, c.site.Tile); c.site.Notify_CaravanFormed(caravan);
            caravan.pather.StartPath(c.map.Tile, new CaravanArrivalAction_Enter(c.map.Parent));
            Report();
        }
    }
}
