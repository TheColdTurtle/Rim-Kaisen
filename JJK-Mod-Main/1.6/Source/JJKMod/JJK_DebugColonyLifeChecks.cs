using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_DebugColonyLifeChecks
    {
        private static IntVec3 Clear(Map map)
        {
            var center = GenRadial.RadialCellsAround(map.Center, 70, true).First(c =>
                CellRect.CenteredOn(c, 12).Cells.All(x => x.InBounds(map) && x.GetFirstPawn(map) == null));
            foreach (var cell in CellRect.CenteredOn(center, 12).Cells)
            {
                foreach (var thing in cell.GetThingList(map).ToList()) if (!(thing is Pawn)) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
            }
            return center;
        }
        private static Pawn Human(Map map, IntVec3 cell, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle(name);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Ratio"), true);
            p.GetComp<CompAbilities>().RecheckAbilities(); GenSpawn.Spawn(p, cell, map);
            GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Grade2);
            p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy = 200;
            return p;
        }
        private static T Building<T>(string name, Map map, IntVec3 cell) where T : Building
        {
            var b = (T)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)); b.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(b, cell, map); return b;
        }
        [DebugAction("Rim-Kaisen", "Run colony life checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Queue() => LongEventHandler.QueueLongEvent(Run, "GeneratingMapForNewEncounter", false, null);
        private static void Run()
        {
            int pass = 0, fail = 0;
            Action<bool, string> check = (ok, text) => { if (ok) pass++; else fail++; Log.Message("[JJK Colony Life QA] " + (ok ? "PASS " : "FAIL ") + text); };
            var gc = GameComponent_ColonyLife.Instance; var contracts = GameComponent_Jujutsu.Instance;
            var made = new List<Thing>(); var missions = new List<JujutsuContract>();
            Map home = Find.CurrentMap; Faction school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
            var standing = contracts.Standing(school); int oldRep = standing.reputation, oldOffer = standing.nextOfferTick;
            try
            {
                var cell = Clear(home);
                var p = Human(home, cell, "Colony Life QA"); made.Add(p);
                var mentor = Human(home, cell + IntVec3.West * 3, "Colony Mentor QA"); made.Add(mentor);
                var vault = Building<Building_ContainmentVault>("JJK_ContainmentVault", home, cell + IntVec3.East * 3); made.Add(vault);
                var relic = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_DangerousRelic")); GenSpawn.Spawn(relic, vault.Position, home); made.Add(relic);
                check(vault.Relics == 1 && vault.GetStoreSettings().AllowedToAccept(relic), "vault accepts and counts the actual relic");
                vault.seal = 0.6f; vault.TickRare(); check(vault.seal < 0.6f, "occupied vault seals decay");
                p.Position = vault.Position + IntVec3.West; float ce = p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy;
                check(vault.Maintain(p) && vault.seal == 1 && p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy == ce - 20, "maintenance restores seal and spends 20 CE");
                p.Position = cell + IntVec3.West * 4; check(!vault.Maintain(p), "remote maintenance rejected");
                vault.seal = 0.4f; vault.Breach();
                check(home.mapPawns.FreeColonistsSpawned.Any(x => x.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_VaultNightmares"))), "failing vault causes nightmares");
                var existingPawns = home.mapPawns.AllPawnsSpawned.ToList(); vault.seal = 0; vault.Breach();
                var escaped = home.mapPawns.AllPawnsSpawned.Except(existingPawns).ToList(); made.AddRange(escaped);
                check(escaped.Count > 0, "exhausted seal causes a curse outbreak");
                foreach (var curse in escaped) curse.Destroy();
                foreach (var colonist in home.mapPawns.FreeColonistsSpawned) colonist.mindState.mentalStateHandler.CurState?.RecoverFromState();
                relic.Destroy(); float seal = vault.seal; vault.TickRare(); check(vault.seal == seal, "empty vault has no upkeep");
                var a = Building<Building_CurtainAnchor>("JJK_CurtainAnchor", home, cell + IntVec3.North * 5); made.Add(a);
                var b = Building<Building_CurtainAnchor>("JJK_CurtainAnchor", home, cell + IntVec3.North * 5 + IntVec3.East * 6); made.Add(b);
                a.Fuel.Refuel(10); b.Fuel.Refuel(10); a.enabled = b.enabled = true;
                float fuel = a.Fuel.Fuel; a.TickRare(); check(a.Fuel.Fuel < fuel, "active anchors spend actual fuel");
                var curtains = home.GetComponent<MapComponent_PlayerCurtains>(); curtains.Rebuild();
                check(curtains.Segments.Count(s => s.first == a || s.second == a) == 5, "aligned fueled anchors create five barrier cells");
                var segment = curtains.Segments.First(s => s.first == a || s.second == a);
                check(segment.PawnCanOpen(p), "curse-only curtain admits colonists");
                a.restriction = b.restriction = CurtainRestriction.Everyone;
                check(segment.Blocks(p), "everyone restriction blocks colonists");
                b.enabled = false; check(!segment.Blocks(p), "loss of anchor activity releases passage immediately");
                curtains.Rebuild(); check(!curtains.Segments.Any(s => s.first == a || s.second == a), "inactive link is removed");
                b.enabled = true; curtains.Rebuild(); b.Destroy(); check(!curtains.Segments.Any(s => s.first == a || s.second == a), "destroying anchor removes supported curtain");
                a.Destroy();
                check(gc.Enroll(p) && !gc.Enroll(p), "school enrollment is unique");
                var student = gc.students.First(s => s.pawn == p); student.mentor = mentor;
                check(!gc.Graduate(student), "graduation cannot bypass lessons and missions");
                gc.EnsureClans(); var clan = gc.clans[0]; int oldStanding = clan.standing;
                clan.deadline = Find.TickManager.TicksGame + 10000; clan.demand = 2; clan.candidate = p;
                check(gc.Resolve(clan, home, true) && clan.leader == p && p.Faction == Faction.OfPlayer && clan.standing == oldStanding + 15,
                    "clan leadership choice changes standing without transferring colonist");
                check(!gc.Resolve(clan, home, true), "clan petition cannot resolve twice");
                clan.leader = null;
                p.mindState.mentalStateHandler.CurState?.RecoverFromState();
                mentor.mindState.mentalStateHandler.CurState?.RecoverFromState();
                foreach (ContractKind kind in new[] { ContractKind.Investigation, ContractKind.Evacuation, ContractKind.Haunting })
                foreach (HauntedRule rule in kind == ContractKind.Haunting ? Enum.GetValues(typeof(HauntedRule)).Cast<HauntedRule>() : new[] { HauntedRule.Hospital })
                {
                    standing.nextOfferTick = 0; standing.reputation = 100;
                    var c = ExorcismExpeditions.Accept(school, home, 1, kind); missions.Add(c); c.colony.rule = rule;
                    check(c != null && !c.ObjectiveMet(), kind + " unvisited assignment cannot complete");
                    GetOrGenerateMapUtility.GetOrGenerateMap(c.site.Tile, new IntVec3(100, 1, 100), null);
                    var map = c.site.Map;
                    check(c.siteGenerated && c.colony.nodes.Count > 0, kind + " " + rule + " generates actual site and objective nodes");
                    p.DeSpawn(); GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(c.colony.center, map, 5), map);
                    mentor.DeSpawn(); GenSpawn.Spawn(mentor, CellFinder.RandomClosewalkCellNear(p.Position, map, 2), map);
                    ExorcismExpeditions.TrackParty(c); gc.TrackSupervision(c);
                    check(c.supervisedStudents.Contains(p), "co-located student and assigned mentor earn supervision eligibility");
                    if (kind == ContractKind.Evacuation)
                    {
                        var beacon = c.colony.nodes.First(n => n.kind == 4);
                        var civilian = c.colony.civilians[0];
                        check(!ColonyMissions.Extract(c, civilian, p), "civilian cannot extract remotely");
                        p.Position = beacon.Position + IntVec3.East;
                        for (int i = 0; i < 2; i++)
                        {
                            civilian = c.colony.civilians[0]; civilian.Position = beacon.Position + IntVec3.West;
                            check(ColonyMissions.Extract(c, civilian, p), "living civilian extracted at beacon");
                            check(!ColonyMissions.Extract(c, civilian, p), "duplicate civilian extraction rejected");
                        }
                        check(ColonyMissions.SiteComplete(c) && c.Remaining > 0, "evacuation succeeds with living enemies");
                    }
                    else
                    {
                        check(!c.colony.revealed && c.Remaining == 0 && !ColonyMissions.SiteComplete(c), "concealed curse cannot auto-complete");
                        c.colony.clues = 7; ColonyMissions.Reveal(c);
                        check(c.Remaining > 0 && c.threats.All(t => t.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_ExposedCurse"))), "complete investigation reveals vulnerable curses");
                        int count = c.threats.Count; ColonyMissions.Reveal(c); check(count == c.threats.Count, "reveal is one-shot");
                        foreach (var enemy in c.threats.Where(t => !t.Dead).ToList()) enemy.Kill(null);
                        if (kind == ContractKind.Haunting)
                        {
                            check(!ColonyMissions.SiteComplete(c), rule + " killing curses does not break the haunting");
                            var ward = c.colony.nodes.First(n => n.kind == 3);
                            if (rule == HauntedRule.Hospital)
                                foreach (var h in c.colony.civilians[0].health.hediffSet.hediffs.Where(h => h.TendableNow()).ToList()) h.Tended(1, 1, 1);
                            if (rule == HauntedRule.Shrine) { c.colony.offering.DeSpawn(); GenSpawn.Spawn(c.colony.offering, ward.Position + IntVec3.East, map); }
                            if (rule == HauntedRule.Tunnel) foreach (var stone in c.colony.keystones) stone.Destroy();
                            if (rule == HauntedRule.School) { p.Position = ward.Position + IntVec3.East; mentor.Position = ward.Position + IntVec3.West; }
                            check(ColonyMissions.RuleSatisfied(c, p), rule + " accepts its distinct completed condition");
                            c.colony.broken = true;
                        }
                    }
                    check(!c.ObjectiveMet(), kind + " requires participant return home");
                    p.DeSpawn(); GenSpawn.Spawn(p, cell, home); mentor.DeSpawn(); GenSpawn.Spawn(mentor, cell + IntVec3.West * 2, home);
                    contracts.Evaluate(c); int before = student.missions;
                    check(contracts.Claim(c) && student.missions == before + 1, kind + " return permits reward and supervised mission credit");
                    check(!contracts.Claim(c) && student.missions == before + 1, "claim cannot duplicate reward or school credit");
                    c.state = ContractState.Completed;
                }
                check(RivalWorld.Spawn(gc, home), "recurring rival spawns");
                var rival = gc.rivals.Last(); made.Add(rival.pawn); string id = rival.pawn.ThingID;
                rival.retreating = true;
                check(!RivalWorld.Escape(gc, rival) || gc.HeldRivals.Contains(rival.pawn), "rival escape requires physical map edge");
                if (rival.pawn.Spawned)
                {
                    CellFinder.TryFindRandomEdgeCellWith(x => x.Standable(home), home, CellFinder.EdgeRoadChance_Ignore, out var edge);
                    rival.pawn.Position = edge;
                    check(RivalWorld.Escape(gc, rival), "rival at edge enters persistent pawn storage");
                }
                check(RivalWorld.Spawn(gc, home, rival) && rival.pawn.ThingID == id && rival.encounters == 2 && rival.tactics == 1, "same rival returns with changed tactics");
                rival.pawn.Kill(null); RivalWorld.Tick(gc, Find.TickManager.TicksGame);
                check(rival.ended, "dead rival never resurrects"); gc.rivals.Remove(rival);
            }
            catch (Exception e) { fail++; Log.Error("[JJK Colony Life QA] " + e); }
            finally
            {
                foreach (var t in made) if (t != null && !t.Destroyed) t.Destroy();
                gc.students.RemoveAll(s => s.pawn == null || s.pawn.Destroyed);
                foreach (var c in missions.Where(c => c != null))
                {
                    c.state = ContractState.Failed; contracts.contracts.Remove(c); var site = c.site;
                    LongEventHandler.ExecuteWhenFinished(() => { if (site != null && !site.Destroyed) { if (site.HasMap) site.CheckRemoveMapNow(); else site.Destroy(); } });
                }
                standing.reputation = oldRep; standing.nextOfferTick = oldOffer;
                Current.Game.CurrentMap = home;
                Log.Message("[JJK Colony Life QA] RESULT " + pass + " passed, " + fail + " failed.");
            }
        }
        [DebugAction("Rim-Kaisen", "Stage colony life jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Stage()
        {
            Map map = Find.CurrentMap; var cell = Clear(map);
            var gc = GameComponent_ColonyLife.Instance;
            var desk = Building<Building>("JJK_SchoolDesk", map, cell);
            var mentor = Human(map, cell + IntVec3.West * 2, "School QA Teacher");
            var pupil = Human(map, cell + IntVec3.East * 2, "School QA Pupil");
            var second = Human(map, cell + IntVec3.South * 2, "School QA Second");
            gc.Enroll(pupil); gc.Enroll(second); gc.StartClass(map, mentor);
            var vault = Building<Building_ContainmentVault>("JJK_ContainmentVault", map, cell + IntVec3.North * 5);
            var relic = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_DangerousRelic")); GenSpawn.Spawn(relic, vault.Position, map);
            vault.seal = 0.6f;
            var worker = Human(map, vault.Position + IntVec3.West, "Vault QA Worker");
            worker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_MaintainVault"), vault));
            Log.Message("[JJK Colony Life QA] STAGED real class and vault jobs. Run 3,000 ticks, then Verify colony life jobs.");
        }
        [DebugAction("Rim-Kaisen", "Verify colony life jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Verify()
        {
            var gc = GameComponent_ColonyLife.Instance;
            foreach (string name in new[] { "School QA Pupil", "School QA Second" })
            {
                var s = gc.students.FirstOrDefault(s => s.pawn?.Name?.ToStringShort == name);
                Log.Message("[JJK Colony Life QA] " + (s?.lessons == 1 ? "PASS" : "FAIL") + " actual class job for " + name + "; lessons=" + s?.lessons);
            }
            var vault = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_ContainmentVault")).OfType<Building_ContainmentVault>().FirstOrDefault();
            Log.Message("[JJK Colony Life QA] " + (vault?.seal > 0.95f ? "PASS" : "FAIL") + " actual maintenance job; seal=" + vault?.seal);
        }
        [DebugAction("Rim-Kaisen", "Stage colony mission jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void QueueMissionJobs() => LongEventHandler.QueueLongEvent(StageMissionJobs, "GeneratingMapForNewEncounter", false, null);
        private static void StageMissionJobs()
        {
            Map home = Find.Maps.First(m => m.IsPlayerHome); var gc = GameComponent_Jujutsu.Instance;
            foreach (var kind in new[] { ContractKind.Haunting, ContractKind.Evacuation })
            {
                var school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer) && !gc.contracts.Any(c => c.school == f && ColonyMissions.Active(c)));
                gc.Standing(school).nextOfferTick = 0;
                var c = ExorcismExpeditions.Accept(school, home, 1, kind); c.colony.rule = HauntedRule.Shrine;
                GetOrGenerateMapUtility.GetOrGenerateMap(c.site.Tile, new IntVec3(100, 1, 100), null);
                Map map = c.site.Map;
                if (kind == ContractKind.Haunting)
                {
                    foreach (var node in c.colony.nodes.Where(n => n.kind < 3))
                    {
                        var p = Human(map, CellFinder.RandomClosewalkCellNear(node.Position, map, 2), "Mission QA Investigator " + node.kind);
                        var menu = node.GetFloatMenuOptions(p).FirstOrDefault(o => o.action != null);
                        if (menu == null) Log.Error("[JJK Colony Life QA] missing investigation float menu");
                        else menu.action();
                    }
                }
                else
                {
                    // Keep this fixture focused on job mechanics, including a downed survivor, without combat interruption.
                    foreach (var enemy in c.threats.ToList()) if (!enemy.Dead && !enemy.Destroyed) enemy.Kill(null);
                    var beacon = c.colony.nodes.First(n => n.kind == 4);
                    for (int i = 0; i < 2; i++)
                    {
                        var civilian = c.colony.civilians[i];
                        civilian.Position = CellFinder.RandomClosewalkCellNear(beacon.Position, map, 7);
                        if (i == 1) civilian.health.AddHediff(HediffDefOf.Anesthetic);
                        var p = Human(map, CellFinder.RandomClosewalkCellNear(civilian.Position, map, 2), "Mission QA Escort " + i);
                        var menu = civilian.GetFloatMenuOptions(p).FirstOrDefault(o => o.Label.Contains("civilian to extraction"));
                        if (menu == null) Log.Error("[JJK Colony Life QA] missing civilian escort float menu");
                        else menu.action();
                    }
                }
            }
            var life = GameComponent_ColonyLife.Instance;
            RivalWorld.Spawn(life, home); var rival = life.rivals.Last(); rival.retreating = true;
            RivalWorld.Escape(life, rival);
            Log.Message("[JJK Colony Life QA] STAGED mission jobs and stored rival " + rival.pawn.ThingID);
            Current.Game.CurrentMap = home;
        }
        [DebugAction("Rim-Kaisen", "Continue colony mission jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ContinueMissionJobs()
        {
            var gc = GameComponent_Jujutsu.Instance;
            foreach (var c in gc.contracts.Where(c => ColonyMissions.Active(c) && c.colony != null))
            {
                if (c.kind == ContractKind.Evacuation)
                {
                    Log.Message("[JJK Colony Life QA] " + (c.colony.extracted == 2 ? "PASS" : "FAIL") + " actual walking and carried civilian escort jobs; extracted=" + c.colony.extracted);
                    continue;
                }
                Log.Message("[JJK Colony Life QA] " + (c.colony.Found == 3 ? "PASS" : "FAIL") + " actual investigation jobs; evidence=" + c.colony.Found);
                if (c.colony.broken) continue;
                foreach (var enemy in c.threats.ToList()) if (enemy != null && !enemy.Dead && !enemy.Destroyed) enemy.Kill(null);
                var ward = c.colony.nodes.First(n => n.kind == 3);
                var p = c.site.Map.mapPawns.FreeColonistsSpawned.First();
                p.jobs.StopAll();
                var option = ward.GetFloatMenuOptions(p).FirstOrDefault(o => o.Label == "Return stolen offering");
                if (option == null) Log.Error("[JJK Colony Life QA] missing return offering order");
                else option.action();
            }
            var life = GameComponent_ColonyLife.Instance;
            var rival = life.rivals.FirstOrDefault(r => !r.ended);
            Log.Message("[JJK Colony Life QA] " + (rival != null && life.HeldRivals.Contains(rival.pawn) ? "PASS" : "FAIL") + " stored rival survived save/load; identity=" + rival?.pawn?.ThingID);
        }
        [DebugAction("Rim-Kaisen", "Start colony ritual job", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartRitual()
        {
            var c = GameComponent_Jujutsu.Instance.contracts.First(c => c.kind == ContractKind.Haunting && ColonyMissions.Active(c));
            var p = c.site.Map.mapPawns.FreeColonistsSpawned.First();
            Log.Message("[JJK Colony Life QA] " + (ColonyMissions.RuleSatisfied(c, p) ? "PASS" : "FAIL") + " actual offering return job");
            var ward = c.colony.nodes.First(n => n.kind == 3);
            var option = ward.GetFloatMenuOptions(p).FirstOrDefault(o => o.Label.StartsWith("Break the curse"));
            if (option == null) Log.Error("[JJK Colony Life QA] missing break curse order"); else option.action();
        }
        [DebugAction("Rim-Kaisen", "Verify colony mission jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VerifyMissions()
        {
            var c = GameComponent_Jujutsu.Instance.contracts.First(c => c.kind == ContractKind.Haunting && ColonyMissions.Active(c));
            Log.Message("[JJK Colony Life QA] " + (c.colony.broken && c.site.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_DangerousRelic")).Count == 1 ? "PASS" : "FAIL") + " actual ritual survived save/load and produced one relic; broken=" + c.colony.broken);
        }
        [DebugAction("Rim-Kaisen", "Retry colony extraction jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RetryExtraction()
        {
            var c = GameComponent_Jujutsu.Instance.contracts.First(c => c.kind == ContractKind.Evacuation && ColonyMissions.Active(c));
            var beacon = c.colony.nodes.First(n => n.kind == 4); Map map = c.site.Map;
            for (int i = 0; i < 2; i++)
            {
                var civilian = c.colony.civilians[i];
                civilian.jobs.StopAll(); civilian.Position = CellFinder.RandomClosewalkCellNear(beacon.Position, map, 7);
                if (i == 1 && !civilian.Downed) civilian.health.AddHediff(HediffDefOf.Anesthetic);
                var p = map.mapPawns.FreeColonistsSpawned.First(p => p.Name.ToStringShort == "Mission QA Escort " + i);
                p.jobs.StopAll(); p.Position = CellFinder.RandomClosewalkCellNear(civilian.Position, map, 2);
                var menu = civilian.GetFloatMenuOptions(p).First(o => o.Label.Contains("civilian to extraction")); menu.action();
            }
        }
    }
}
