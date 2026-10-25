using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Contracts keep their home/reward map. A separate Site owns the encounter map.
    public static class ExorcismExpeditions
    {
        public const int Duration = 1200000; // Twenty days includes the return journey.
        public static JujutsuContract Accept(Faction school, Map home, int tier, ContractKind kind)
        {
            var gc = GameComponent_Jujutsu.Instance;
            if (kind == ContractKind.Boss || (kind == ContractKind.Sealing && tier != 3) || !gc.CanAccept(school, home, tier, kind)) return null;
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, home.Tile, 2, 8, selectLandmarkChance: 0f))
            { Messages.Message("JJK_ExpeditionNoTile".Translate(), MessageTypeDefOf.RejectInput, false); return null; }
            var site = (ExorcismSite)SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("JJK_HauntedSettlement"), tile,
                null, false, 180f * tier, DefDatabase<WorldObjectDef>.GetNamed("JJK_ExorcismSite"));
            var c = new JujutsuContract { school = school, map = home, tier = tier, kind = kind, expedition = true,
                site = site, expires = Find.TickManager.TicksGame + Duration, rewardDef = gc.RewardFor(school, tier) };
            if (ColonyMissions.Special(kind)) c.colony = new ColonyMissionData { rule = (HauntedRule)Rand.Range(0, 4) };
            if (kind == ContractKind.Sealing) c.sealing = new SealingData();
            site.customLabel = c.Title; site.sitePartsKnown = true;
            gc.contracts.Add(c); Find.WorldObjects.Add(site);
            Find.LetterStack.ReceiveLetter(c.Title, "JJK_ExpeditionAccepted".Translate(home.Parent.LabelCap), LetterDefOf.NeutralEvent, site);
            return c;
        }
        public static bool Returned(Pawn p, JujutsuContract c) => p != null && !p.Dead && !p.Destroyed
            && p.Faction == Faction.OfPlayer && p.MapHeld == c.map;
        public static bool ObjectiveMet(JujutsuContract c)
        {
            if (c.sealing != null) return SealingCampaign.ObjectiveMet(c);
            if (c.campaign != null && c.campaign.stage != SchoolStage.Boss) return false;
            if (ColonyMissions.Special(c.kind)) return c.siteGenerated && ColonyMissions.SiteComplete(c)
                && c.map != null && c.participants.Any(p => Returned(p, c));
            if (!c.siteGenerated || !c.siteCleared || c.Remaining > 0 || c.map == null || !c.participants.Any(p => Returned(p, c))) return false;
            if (c.kind == ContractKind.Rescue) return Returned(c.rescue, c);
            if (c.kind == ContractKind.Recovery) return c.artifact != null && !c.artifact.Destroyed && c.artifact.MapHeld == c.map;
            return true;
        }
        public static void TrackParty(JujutsuContract c)
        {
            if (c.site?.HasMap != true) return;
            foreach (Pawn p in c.site.Map.mapPawns.FreeColonistsSpawned)
                if (!c.participants.Contains(p) && p != c.rescue) c.participants.Add(p);
        }
        public static void Evaluate(JujutsuContract c)
        {
            var gc = GameComponent_Jujutsu.Instance;
            if (c.map == null || !Find.Maps.Contains(c.map) || c.school == null || c.school.HostileTo(Faction.OfPlayer)
                || c.school.defeated || Find.TickManager.TicksGame >= c.expires
                || (!c.siteCleared && (c.site == null || c.site.Destroyed))
                || (c.siteGenerated && c.kind == ContractKind.Rescue && (c.rescue == null || c.rescue.Dead || c.rescue.Destroyed))
                || (c.siteGenerated && c.kind == ContractKind.Recovery && (c.artifact == null || c.artifact.Destroyed)))
            { gc.Fail(c); return; }
            TrackParty(c);
            GameComponent_ColonyLife.Instance?.TrackSupervision(c);
            ColonyMissions.Tick(c);
            if (c.sealing != null)
            {
                SealingCampaign.Tick(c);
                if (c.state != ContractState.Failed) c.state = ObjectiveMet(c) ? ContractState.Ready : ContractState.Active;
                return;
            }
            if (c.state == ContractState.Failed) return;
            if (c.siteGenerated && c.participants.Count > 0 && (ColonyMissions.Special(c.kind) ? ColonyMissions.SiteComplete(c) : c.Remaining == 0) && !c.siteCleared)
            {
                c.siteCleared = true;
                Messages.Message("JJK_ExpeditionCleared".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            bool ready = ObjectiveMet(c);
            if (ready && c.state != ContractState.Ready) Messages.Message("JJK_ContractReady".Translate(c.Title), MessageTypeDefOf.PositiveEvent);
            c.state = ready ? ContractState.Ready : ContractState.Active;
        }
        public static string Progress(JujutsuContract c)
        {
            if (c.sealing != null) return SealingCampaign.Progress(c);
            if (ColonyMissions.Special(c.kind)) return ColonyMissions.Progress(c);
            if (!c.siteGenerated) return "JJK_ExpeditionUnvisited".Translate();
            if (!c.siteCleared) return "JJK_ExpeditionThreats".Translate(c.Remaining);
            if (c.kind == ContractKind.Rescue) return "JJK_ExpeditionRescueReturn".Translate(c.map.Parent.LabelCap);
            if (c.kind == ContractKind.Recovery) return "JJK_ExpeditionArtifactReturn".Translate(c.map.Parent.LabelCap);
            return "JJK_ExpeditionReturn".Translate(c.map.Parent.LabelCap);
        }
        public static bool Recruit(JujutsuContract c)
        {
            Evaluate(c);
            if (!c.siteCleared || c.state == ContractState.Failed || c.state == ContractState.Completed || c.rescue?.MapHeld == null
                || c.rescue.Dead || c.rescue.Faction == Faction.OfPlayer || !c.rescue.MapHeld.mapPawns.FreeColonistsSpawned.Any()) return false;
            if (c.rescue.ParentHolder is Building_SealedRefuge refuge) refuge.EjectContents();
            if (!c.rescue.Spawned) return false;
            var stupor = c.rescue.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic);
            if (stupor != null) c.rescue.health.RemoveHediff(stupor);
            c.rescue.SetFaction(Faction.OfPlayer); c.rescue.jobs.StopAll();
            Messages.Message("JJK_ExpeditionRescued".Translate(c.rescue.LabelShortCap), c.rescue, MessageTypeDefOf.PositiveEvent);
            return true;
        }
    }

    public class ExorcismSite : Site
    {
        public JujutsuContract Contract => GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => c.site == this);
        public bool ActiveContract => Contract != null && (Contract.state == ContractState.Active || Contract.state == ContractState.Ready);
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            // Preserve an unfinished encounter when the caravan retreats; no regenerating loot/enemies.
            if (ActiveContract) { alsoRemoveWorldObject = false; return false; }
            return base.ShouldRemoveMapNow(out alsoRemoveWorldObject);
        }
        public override void Notify_CaravanFormed(Caravan caravan)
        {
            if (Contract is JujutsuContract c)
            {
                foreach (var p in caravan.PawnsListForReading.Where(p => p.IsColonist && p != c.rescue))
                    if (!c.participants.Contains(p)) c.participants.Add(p);
                ExorcismExpeditions.Evaluate(c);
            }
            base.Notify_CaravanFormed(caravan);
        }
        public override string GetInspectString() => base.GetInspectString() + (Contract == null ? "" : "\n" + ExorcismExpeditions.Progress(Contract)
            + "\n" + "JJK_ExpeditionTime".Translate(Mathf.Max(0, Contract.expires - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
    }

    public class SitePartWorker_HauntedSettlement : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            var c = (map.Parent as ExorcismSite)?.Contract;
            if (c == null || c.siteGenerated) return;
            IntVec3 center = GenRadial.RadialCellsAround(map.Center, 60, true).Where(x => x.InBounds(map) && !x.CloseToEdge(map, 15)
                && GenRadial.RadialCellsAround(x, 5, true).All(p => p.InBounds(map) && p.Standable(map))).DefaultIfEmpty(IntVec3.Invalid).First();
            if (!center.IsValid) center = map.AllCells.Where(x => !x.CloseToEdge(map, 15) && x.Standable(map))
                .OrderBy(x => x.DistanceToSquared(map.Center)).DefaultIfEmpty(map.Center).First();
            // Very mountainous maps may have no complete clearing; make the authored objective footprint usable.
            if (c.kind == ContractKind.Sealing)
                foreach (var cell in GenRadial.RadialCellsAround(center, 10, true).Where(x => x.InBounds(map)))
                {
                    if (cell.GetEdifice(map) is Building rock && rock.def.building?.isNaturalRock == true) rock.Destroy();
                    if (!cell.Standable(map)) map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
            if (c.kind == ContractKind.Sealing) { SealingCampaign.Generate(c, map, center); return; }
            if (ColonyMissions.Special(c.kind)) { ColonyMissions.Generate(c, map, center); return; }
            // Three ruined dwellings, with open entrances and no roofs to trap the party.
            foreach (int offset in new[] { -10, 0, 10 })
            {
                IntVec3 origin = center + new IntVec3(offset, 0, 0);
                var rect = CellRect.CenteredOn(origin, 3);
                if (!rect.Cells.All(p => p.InBounds(map) && p.Standable(map))) continue;
                foreach (var cell in rect.Cells)
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                    if (rect.EdgeCells.Contains(cell) && cell != new IntVec3(origin.x, 0, rect.minZ) && Rand.Chance(0.75f))
                    {
                        var wall = ThingMaker.MakeThing(ThingDefOf.Wall, DefDatabase<ThingDef>.GetNamed("BlocksSandstone"));
                        GenSpawn.Spawn(wall, cell, map); wall.HitPoints = Mathf.Max(1, wall.MaxHitPoints / 2);
                    }
                }
            }
            c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, CellFinder.RandomClosewalkCellNear(center, map, 7), 180f * c.tier, 12));
            if (c.kind == ContractKind.Rescue)
            {
                c.rescue = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_SorcererApprentice"),
                    c.school, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                c.rescue.health.AddHediff(HediffDefOf.Anesthetic);
                var refuge = (Building_SealedRefuge)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_SealedRefuge"));
                GenSpawn.Spawn(refuge, CellFinder.RandomClosewalkCellNear(center, map, 3), map);
                refuge.TryAcceptThing(c.rescue);
            }
            if (c.kind == ContractKind.Recovery)
            {
                c.artifact = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_SealedMissionObject"));
                GenPlace.TryPlaceThing(c.artifact, center, map, ThingPlaceMode.Near); c.artifact.SetForbidden(false);
            }
            c.siteGenerated = true;
            if (c.threats.Count == 0) GameComponent_Jujutsu.Instance.Fail(c);
        }
    }
    public class Building_SealedRefuge : Building_Casket
    {
        private JujutsuContract Contract => GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => c.expedition
            && c.rescue != null && c.rescue.ParentHolder == this);
        public override bool CanOpen => Contract?.siteCleared == true;
        public override void Open() { if (Contract is JujutsuContract c) ExorcismExpeditions.Recruit(c); }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_ExpeditionRescueGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var value in values) yield return value;
            var c = GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => c.expedition && c.kind == ContractKind.Rescue && c.rescue == __instance
                && c.state == ContractState.Active && __instance.Faction != Faction.OfPlayer);
            if (c == null) yield break;
            var cmd = new Command_Action { defaultLabel = "JJK_ExpeditionRecruit".Translate(), defaultDesc = "JJK_ExpeditionRecruitDesc".Translate(),
                icon = TexCommand.Draft, action = () => ExorcismExpeditions.Recruit(c) };
            if (!c.siteCleared || !__instance.Spawned || !__instance.Map.mapPawns.FreeColonistsSpawned.Any()) cmd.Disable("JJK_ExpeditionSecureFirst".Translate());
            yield return cmd;
        }
    }
}
