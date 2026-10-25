using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public enum SchoolStage { Investigation, Rescue, Pursuit, Boss }
    public class SchoolCampaignData : IExposable
    {
        public SchoolStage stage;
        public bool prepared, cutOff;
        public int rescued;
        public void ExposeData()
        {
            Scribe_Values.Look(ref stage, "stage"); Scribe_Values.Look(ref prepared, "prepared");
            Scribe_Values.Look(ref cutOff, "cutOff"); Scribe_Values.Look(ref rescued, "rescued");
        }
    }
    public static class SchoolCampaign
    {
        public static JujutsuContract Begin(Faction school, Map home)
        {
            var c = ExorcismExpeditions.Accept(school, home, 3, ContractKind.Investigation);
            if (c == null) return null;
            c.campaign = new SchoolCampaignData(); c.expires = Find.TickManager.TicksGame + 1800000;
            c.site.customLabel = c.Title;
            return c;
        }
        public static bool PartyPresent(JujutsuContract c) => c.site?.HasMap == true
            && c.site.Map.mapPawns.FreeColonistsSpawned.Any(ColonyLifeUtility.Available);
        public static bool CanAdvance(JujutsuContract c, bool cutOff = false)
        {
            if (!ColonyMissions.Active(c) || c.campaign == null || !c.siteGenerated || !PartyPresent(c)) return false;
            switch (c.campaign.stage)
            {
                case SchoolStage.Investigation: return ColonyMissions.SiteComplete(c);
                case SchoolStage.Rescue: return c.colony.extracted >= 2;
                case SchoolStage.Pursuit: return cutOff || c.Remaining == 0;
                default: return false;
            }
        }
        public static bool Advance(JujutsuContract c, bool cutOff = false)
        {
            if (!CanAdvance(c, cutOff)) return false;
            var d = c.campaign; Map map = c.site.Map;
            IntVec3 center = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
            if (d.stage == SchoolStage.Investigation)
            {
                // Snapshot at reveal, not after late clue collection.
                d.stage = SchoolStage.Rescue; c.kind = ContractKind.Evacuation;
                foreach (var n in c.colony.nodes.Where(n => n?.Spawned == true).ToList()) n.Destroy();
                c.colony = new ColonyMissionData(); ColonyMissions.Generate(c, map, center);
                if (d.prepared) c.colony.nextWave += 15000;
            }
            else if (d.stage == SchoolStage.Rescue)
            {
                d.rescued = c.colony.extracted; d.stage = SchoolStage.Pursuit; c.kind = ContractKind.Infestation;
                foreach (var n in c.colony.nodes.Where(n => n?.Spawned == true).ToList()) n.Destroy();
                c.colony = null;
                // Surviving rescue enemies remain on the map and in the threat ledger.
                c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, center, d.rescued == 3 ? 240 : 480, 8));
            }
            else
            {
                d.cutOff = cutOff; d.stage = SchoolStage.Boss; c.kind = ContractKind.Boss;
                var boss = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Boss_Hanami_Kind"));
                GenSpawn.Spawn(boss, center, map); c.threats.Add(boss);
                if (d.prepared) boss.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_ExposedCurse"));
                if (!cutOff) c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, center, d.rescued == 3 ? 180 : 360, 5));
            }
            c.siteCleared = false; c.state = ContractState.Active;
            GameComponent_CampaignHistory.Instance?.Add(null, "JJK_RecordStage", Progress(c));
            Find.LetterStack.ReceiveLetter(c.Title, Progress(c), LetterDefOf.ThreatBig, new LookTargets(center, map));
            return true;
        }
        public static string Progress(JujutsuContract c) => "JJK_SchoolStage".Translate(
            ("JJK_Stage" + c.campaign.stage).Translate(), c.campaign.prepared ? "JJK_Yes".Translate() : "JJK_No".Translate(), c.campaign.rescued)
            + "\n" + ("JJK_StageHelp" + c.campaign.stage).Translate();
        public static void Draw(Listing_Standard list, JujutsuContract c)
        {
            if (c.campaign == null) return;
            list.Label(Progress(c));
            if (CanAdvance(c) && list.ButtonText("JJK_SchoolAdvance".Translate())) Advance(c);
            if (c.campaign.stage == SchoolStage.Pursuit && CanAdvance(c, true)
                && list.ButtonText("JJK_SchoolCutOff".Translate())) Advance(c, true);
        }
    }
}
