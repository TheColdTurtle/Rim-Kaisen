using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public static class JJK_DebugCampaignPresentation
    {
        private static Pawn Human(Map map, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle(name);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.GetComp<CompAbilities>().RecheckAbilities();
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
            SealingCampaign.Energy(p).energy = 200;
            return p;
        }
        private static void Move(Pawn p, Map map, IntVec3 near)
        { if (p.Spawned) p.DeSpawn(); GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(near, map, 2), map); }
        private static void Clear(JujutsuContract c)
        { foreach (var p in c.threats.Where(p => p != null && !p.Dead && !p.Destroyed).ToList()) p.Kill(null); }
        private static Map Generate(JujutsuContract c) => GetOrGenerateMapUtility.GetOrGenerateMap(c.site.Tile, new IntVec3(100, 1, 100), null);
        [DebugAction("Rim-Kaisen", "Run campaign presentation checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Queue() => LongEventHandler.QueueLongEvent(Run, "GeneratingMapForNewEncounter", false, null);
        private static void Run()
        {
            Map home = Find.Maps.First(m => m.IsPlayerHome); var gc = GameComponent_Jujutsu.Instance;
            var original = gc.contracts; gc.contracts = new List<JujutsuContract>();
            var cg = GameComponent_CullingGame.Instance; var oldCulling = cg.data; cg.data = new CullingData();
            var school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
            var standing = gc.Standing(school); int rep = standing.reputation, offer = standing.nextOfferTick;
            var created = new List<Pawn>(); int passed = 0, failed = 0;
            Action<bool, string> check = (ok, label) => { if (ok) passed++; else failed++; Log.Message("[JJK Campaign Presentation QA] " + (ok ? "PASS " : "FAIL ") + label); };
            try
            {
                standing.reputation = 100; standing.nextOfferTick = 0;
                var c = SchoolCampaign.Begin(school, home);
                check(c?.campaign != null && c.expires - Find.TickManager.TicksGame == 1800000, "school operation creates 30-day expedition");
                check(!c.ObjectiveMet() && !SchoolCampaign.Advance(c), "unvisited school stage cannot advance or pay");
                var map = Generate(c); var p = Human(map, "Campaign QA Lead"); created.Add(p); ExorcismExpeditions.TrackParty(c);
                check(c.campaign.stage == SchoolStage.Investigation && c.colony.nodes.Count == 4, "investigation site has usable clue objectives");
                check(!SchoolCampaign.Advance(c), "concealed curse cannot be bypassed");
                c.colony.clues = 7; ColonyMissions.Reveal(c);
                check(c.campaign.prepared && c.threats.Count > 0, "full evidence snapshots preparation and spawns threats");
                check(!SchoolCampaign.Advance(c), "living investigation threats block advance");
                Clear(c); gc.Evaluate(c);
                check(c.siteCleared && c.state == ContractState.Active && !gc.Claim(c), "intermediate clearance never pays");
                check(SchoolCampaign.Advance(c) && c.kind == ContractKind.Evacuation && c.colony.civilians.Count == 3, "rescue stage generated");
                check(c.colony.nextWave - Find.TickManager.TicksGame == 30000, "investigation preparation delays rescue reinforcement");
                var beacon = c.colony.nodes.First(n => n.kind == 4);
                Move(p, map, beacon.Position);
                foreach (var civilian in c.colony.civilians.ToList())
                {
                    civilian.Position = beacon.Position; p.Position = CellFinder.RandomClosewalkCellNear(beacon.Position, map, 1);
                    check(ColonyMissions.Extract(c, civilian, p), "civilian extraction uses real objective API");
                }
                check(SchoolCampaign.Advance(c) && c.campaign.rescued == 3 && c.campaign.stage == SchoolStage.Pursuit, "rescue decision carried into pursuit");
                int surviving = c.Remaining;
                check(surviving > 0 && !SchoolCampaign.CanAdvance(c), "pursuit threats block cautious advancement");
                check(SchoolCampaign.Advance(c, true) && c.Remaining == surviving + 1, "cutoff preserves survivors and adds boss without fresh guards");
                var boss = c.threats.Last();
                check(boss.TryGetComp<CompBossCurse>() != null && boss.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_ExposedCurse")), "investigation weakens real final boss");
                Clear(c); gc.Evaluate(c); check(!gc.Claim(c), "boss victory still requires return");
                Move(p, home, home.Center); gc.Evaluate(c);
                check(gc.Claim(c) && !gc.Claim(c), "school operation pays once on return");
                check(GameComponent_CampaignHistory.Instance.For(p).contracts == 1, "completed operation increments participant record");

                foreach (SealRoute route in new[] { SealRoute.Defend, SealRoute.Transport })
                {
                    standing.nextOfferTick = 0;
                    c = ExorcismExpeditions.Accept(school, home, 3, ContractKind.Sealing); map = Generate(c);
                    Move(p, map, map.Center); var helper = Human(map, "Campaign QA Support " + route); created.Add(helper);
                    ExorcismExpeditions.TrackParty(c); var d = c.sealing; var anchor = d.anchors[0];
                    Move(p, map, anchor.Position); Move(helper, map, anchor.Position);
                    d.enemy.Position = CellFinder.RandomClosewalkCellNear(anchor.Position, map, 3);
                    check(d.anchors.Count == 3 && d.enemy.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_SealOverwhelming")), route + " generates overwhelming enemy and three anchors");
                    check(!SealingCampaign.Seal(c, p, anchor), "uncharged anchors reject capture");
                    foreach (var a in d.anchors) a.activated = true;
                    SealingCampaign.Energy(p).energy = 39;
                    check(!SealingCampaign.CanSeal(c, p, anchor), "insufficient energy blocks capture");
                    SealingCampaign.Energy(p).energy = 200;
                    Move(helper, home, home.Center);
                    check(!SealingCampaign.CanSeal(c, p, anchor), "missing second sorcerer blocks capture");
                    Move(helper, map, anchor.Position);
                    check(SealingCampaign.Seal(c, p, anchor), route + " capture succeeds under full conditions");
                    check(!d.enemy.Spawned && !d.enemy.Dead && d.vessel.GetDirectlyHeldThings().Contains(d.enemy), "prison deep-owns original living enemy");
                    check(SealingCampaign.Energy(p).energy == 160 && !SealingCampaign.Seal(c, p, anchor), "capture charges 40 CE once");
                    check(SealingCampaign.ChooseRoute(c, route) && !SealingCampaign.ChooseRoute(c, route), "route is a permanent one-time choice");
                    if (route == SealRoute.Transport)
                    {
                        d.vessel.DeSpawn(); p.inventory.innerContainer.TryAdd(d.vessel); Move(p, home, home.Center);
                    }
                    check(!c.ObjectiveMet(), "capturing and moving alone cannot pay");
                    for (int wave = 0; wave < 3; wave++)
                    {
                        d.nextWave = 0; SealingCampaign.Tick(c);
                        check(d.waves == wave + 1, route + " defense wave " + (wave + 1));
                        foreach (var guard in c.threats.Where(t => t != d.enemy && !t.Dead && !t.Destroyed).ToList()) guard.Kill(null);
                    }
                    check(!d.delivered, "defeating waves early does not skip defense duration");
                    d.defendedTicks = route == SealRoute.Defend ? 60000 : 30000; SealingCampaign.Tick(c);
                    check(d.delivered && !d.enemy.Dead, route + " completes custody without killing target");
                    Move(p, home, home.Center); gc.Evaluate(c);
                    check(gc.Claim(c) && d.vessel.Destroyed && !gc.Claim(c), route + " pays once and hands prison to school");
                }

                check(cg.Begin(home, true), "culling rules fixture begins");
                var site = cg.data.sites[0]; map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, new IntVec3(100, 1, 100), null);
                Move(p, map, site.anchor.Position); cg.TrackVisitors(site); var player = cg.Player(p); player.points = 500;
                foreach (var rule in new[] { CullingRule.NegotiatedTruce, CullingRule.Bounties, CullingRule.ProtectedAreas })
                {
                    int before = player.points; check(cg.BuyRule(p, rule) && player.points == before - 100 && cg.RuleEnabled(rule), rule + " exact purchase");
                    check(!cg.BuyRule(p, rule) && player.points == before - 100, rule + " duplicate rejected");
                }
                check(CullingSanctuary.Protected(p), "anchor sanctuary includes nearby participant");
                float hp = p.health.summaryHealth.SummaryHealthPercent;
                var result = p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 25, instigator: site.rivals[0]));
                check(result.totalDamageDealt == 0 && p.health.summaryHealth.SummaryHealthPercent == hp, "sanctuary prevents real damage");
                var rival = site.rivals[0]; int score = player.points;
                check(cg.PostBounty(p, rival) && player.points == score - 20 && cg.data.rivalPressure == 2, "bounty stake increases rival response");
                check(!cg.PostBounty(p, rival), "duplicate bounty blocked");
                rival.Kill(new DamageInfo(DamageDefOf.Cut, 9999, instigator: p));
                check(player.points == score + 15, "bounty pays stake plus bonus plus standard kill points");
                foreach (var r in site.rivals.Where(r => !r.Dead).ToList()) r.Kill(null);
                int waveBefore = site.wave;
                check(cg.Negotiate(p, site) && site.wave == waveBefore + 1 && site.truceUsed, "paid truce skips exactly one cleared-site wave");
                check(!cg.Negotiate(p, site), "truce cannot be repeated");
                site.UpdateEncounter(Find.TickManager.TicksGame + 59999);
                check(site.wave == waveBefore + 1, "truce holds next wave for one day");
                site.nextWaveTick = 1; site.UpdateEncounter(Find.TickManager.TicksGame);
                check(site.Remaining == 7, "rival response adds two defenders to later wave");
                site.disrupted = true; check(!CullingSanctuary.Protected(p), "disruption ends sanctuary");

                Move(p, home, home.Center);
                var hist = GameComponent_CampaignHistory.Instance; int flashes = hist.For(p).blackFlashes;
                var target = Human(home, "Campaign QA Impact"); created.Add(target);
                BlackFlash.Trigger(p, target, 1); check(hist.For(p).blackFlashes == flashes + 1, "Black Flash hook increments persistent lifetime count");
                foreach (var name in new[] { "JJK_Domain_Limitless", "JJK_Domain_DisasterFlames", "JJK_Domain_DisasterTides", "JJK_Domain_TenShadows" })
                {
                    var pattern = Enumerable.Range(-10, 21).SelectMany(x => Enumerable.Range(-10, 21).Select(z => DomainInteriors.Pattern(name, x, z))).ToList();
                    check(pattern.Contains(0) && pattern.Contains(1), name + " interior contains distinct floor and motif");
                }
                var ability = new Ability_LapseBlue { pawn = p, def = DefDatabase<TaranMagicFramework.AbilityDef>.AllDefs.First(d => d.abilityClass == typeof(Ability_LapseBlue)) };
                var center = p.Position;
                check(TechniquePreview.Cells(ability, center, out var key).Count == GenRadial.RadialCellsAround(center, 3.5f, true).Count(cel => cel.InBounds(home))
                    && key == "JJK_PreviewPull", "Blue preview uses real 3.5-cell pull geometry");
                foreach (var n in new[] { "Mechamaru", "Absolute", "Communication" })
                    foreach (var dir in new[] { "south", "east", "north" })
                        check(ContentFinder<Texture2D>.Get("JJK/Presentation/" + n + "_" + dir, false) != null, n + " " + dir + " texture resolves");
                foreach (var defName in new[] { "JJK_SealingAnchor", "JJK_SealedEnemy" })
                {
                    var thing = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName));
                    check(thing.Graphic.MatSingle.mainTexture != BaseContent.BadTex, defName + " runtime graphic resolves");
                    thing.Destroy();
                }
            }
            catch (Exception e) { failed++; Log.Error("[JJK Campaign Presentation QA] " + e); }
            finally
            {
                foreach (var p in created.Where(p => !p.Destroyed)) p.Destroy();
                var sites = gc.contracts.Select(c => (Site)c.site).Concat(cg.data.sites).Where(s => s != null).ToList();
                gc.contracts = original; cg.data = oldCulling; standing.reputation = rep; standing.nextOfferTick = offer;
                Current.Game.CurrentMap = home;
                LongEventHandler.ExecuteWhenFinished(() => { foreach (var site in sites) if (!site.Destroyed) { if (site.HasMap) site.CheckRemoveMapNow(); else site.Destroy(); } });
                Log.Message("[JJK Campaign Presentation QA] RESULT " + passed + " passed, " + failed + " failed");
            }
        }
        [DebugAction("Rim-Kaisen", "Stage sealing ritual save QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StageRitual() => LongEventHandler.QueueLongEvent(() =>
        {
            var gc = GameComponent_Jujutsu.Instance; Map home = Find.Maps.First(m => m.IsPlayerHome);
            var school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer) && !gc.contracts.Any(c => c.school == f && ColonyMissions.Active(c)));
            gc.Standing(school).reputation = 100; gc.Standing(school).nextOfferTick = 0;
            var c = ExorcismExpeditions.Accept(school, home, 3, ContractKind.Sealing); var map = Generate(c); var a = c.sealing.anchors[0];
            var p = Human(map, "Sealing Save QA Lead"); var helper = Human(map, "Sealing Save QA Support");
            Move(p, map, a.Position); Move(helper, map, a.Position); helper.drafter.Drafted = true;
            foreach (var anchor in c.sealing.anchors) anchor.activated = true;
            c.sealing.enemy.Position = CellFinder.RandomClosewalkCellNear(a.Position, map, 3);
            c.sealing.enemy.health.AddHediff(HediffDefOf.Anesthetic);
            ExorcismExpeditions.TrackParty(c);
            p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_SealEnemy"), a));
            Current.Game.CurrentMap = map;
            Log.Message("[JJK Campaign Presentation QA] STAGED sealing ritual. Save during the 900-tick job and resume it after loading.");
        }, "GeneratingMapForNewEncounter", false, null);
        [DebugAction("Rim-Kaisen", "Verify sealing ritual save QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VerifyRitual()
        {
            var c = GameComponent_Jujutsu.Instance.contracts.FirstOrDefault(c => c.sealing != null && c.participants.Any(p => p?.Name?.ToStringShort == "Sealing Save QA Lead"));
            bool ok = c?.sealing.captured == true && c.sealing.vessel.ContainsEnemy && c.sealing.enemy.ParentHolder == c.sealing.vessel
                && c.sealing.enemy.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_SealOverwhelming"))
                && c.sealing.anchors.All(a => a?.activated == true)
                && GameComponent_CampaignHistory.Instance.entries.Any(e => e.key == "JJK_RecordSeal" && e.name == "Sealing Save QA Lead");
            Log.Message("[JJK Campaign Presentation QA] " + (ok ? "PASS" : "FAIL") + " resumed ritual preserves original live pawn and prison ownership");
            if (ok) Find.WindowStack.Add(new Window_CampaignHistory(c.participants.First(p => p?.Name?.ToStringShort == "Sealing Save QA Lead")));
        }
        [DebugAction("Rim-Kaisen", "Stage campaign interiors QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StageInteriors()
        {
            Map map = Find.Maps.First(m => m.IsPlayerHome); Current.Game.CurrentMap = map;
            var names = new[] { "Limitless", "DisasterFlames", "DisasterTides", "Shrine" };
            for (int i = 0; i < 4; i++)
            {
                var center = map.Center + new IntVec3(i % 2 == 0 ? -22 : 22, 0, i < 2 ? -22 : 22);
                foreach (var cell in GenRadial.RadialCellsAround(center, 17, true).Where(c => c.InBounds(map)))
                {
                    foreach (var t in cell.GetThingList(map).Where(t => t.def.destroyable && (t is Building || t is Plant)).ToList()) t.Destroy();
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null); map.fogGrid.Unfog(cell);
                }
                var caster = Human(map, "Interior QA " + names[i]); Move(caster, map, center); caster.Position = center;
                caster.drafter.Drafted = true;
                map.GetComponent<MapComponent_DomainManager>().Open(caster, DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Domain_" + names[i]));
            }
            foreach (var kind in new[] { "JJK_Mechamaru_Kind", "JJK_AbsolutePuppet_Kind", "JJK_CommunicationPuppet_Kind" })
            {
                int index = kind.Contains("Mechamaru") ? 0 : kind.Contains("Absolute") ? 1 : 2;
                foreach (var rotation in new[] { Rot4.South, Rot4.East, Rot4.North })
                {
                    var puppet = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind), Faction.OfPlayer);
                    GenSpawn.Spawn(puppet, map.Center + new IntVec3(index * 5 - 5, 0, rotation.AsInt * 4 - 4), map);
                    puppet.Rotation = rotation; puppet.TryGetComp<CompCursedCorpse>().standby = true;
                }
            }
            CameraJumper.TryJump(map.Center, map); Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Log.Message("[JJK Campaign Presentation QA] STAGED four domain interiors and all puppet directions, centered at " + map.Center);
        }
        [DebugAction("Rim-Kaisen", "Stage targeting preview QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StagePreview()
        {
            Map map = Find.CurrentMap; var p = Human(map, "Target Preview QA");
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Limitless"), true);
            p.GetComp<CompAbilities>().RecheckAbilities(); p.drafter.Drafted = true;
            var ac = JujutsuTraining.Class(p); var def = DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_LapseBlue");
            var a = ac.GetLearnedAbility(def) ?? ac.LearnAbility(def, false);
            Find.Selector.ClearSelection(); Find.Selector.Select(p); CameraJumper.TryJump(p);
            var command = a.GetCommandActivateAbility(a.Verbs.FirstOrDefault()); command.action();
            Log.Message("[JJK Campaign Presentation QA] " + (Find.Targeter.IsTargeting && TechniquePreview.selected == a ? "PASS" : "FAIL") + " real ability command starts targeting preview");
        }
    }
}
