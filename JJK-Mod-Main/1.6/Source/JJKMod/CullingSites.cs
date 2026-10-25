using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JJKMod
{
    public class CullingSite : Site
    {
        public int number, wave, nextWaveTick;
        public bool finale, generated, disrupted;
        public bool truceUsed;
        public Building_CullingAnchor anchor;
        public List<Pawn> rivals = new List<Pawn>();
        public int WaveLimit => finale ? 3 : 4;
        public int Remaining => rivals.Count(p => p != null && !p.Dead && !p.Destroyed && p.Faction != Faction.OfPlayer);
        public bool Secured => generated && wave >= WaveLimit && Remaining == 0;
        public bool CampaignActive => GameComponent_CullingGame.Instance?.Active == true
            && GameComponent_CullingGame.Instance.data.sites.Contains(this);
        public string Progress => "JJK_CullingProgress".Translate(wave, WaveLimit, Remaining,
            disrupted ? "JJK_CullingDisrupted".Translate() : Secured ? "JJK_CullingReady".Translate() : "JJK_CullingContested".Translate());
        public override string GetInspectString() => base.GetInspectString() + "\n" + Progress;
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            if (CampaignActive) { alsoRemoveWorldObject = false; return false; }
            return base.ShouldRemoveMapNow(out alsoRemoveWorldObject);
        }
        public override void Notify_CaravanFormed(Caravan caravan)
        {
            if (CampaignActive)
                foreach (var p in caravan.PawnsListForReading.Where(GameComponent_CullingGame.LivingPlayer))
                {
                    var record = GameComponent_CullingGame.Instance.Register(p);
                    record.lastSite = this; GameComponent_CullingGame.Instance.Depart(record);
                }
            base.Notify_CaravanFormed(caravan);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return new Command_Action { defaultLabel = "JJK_Kogane".Translate(), defaultDesc = "JJK_CullingOpenDesc".Translate(),
                icon = TexCommand.ClearPrioritizedWork, action = () => Find.WindowStack.Add(new Window_Kogane()) };
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref number, "cullingNumber"); Scribe_Values.Look(ref wave, "cullingWave");
            Scribe_Values.Look(ref nextWaveTick, "cullingNextWave"); Scribe_Values.Look(ref finale, "cullingFinale");
            Scribe_Values.Look(ref generated, "cullingGenerated"); Scribe_Values.Look(ref disrupted, "cullingDisrupted");
            Scribe_Values.Look(ref truceUsed, "truceUsed");
            Scribe_References.Look(ref anchor, "cullingAnchor"); Scribe_Collections.Look(ref rivals, "cullingRivals", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) rivals = rivals ?? new List<Pawn>();
        }
        public void Generate(Map map)
        {
            if (generated || !CampaignActive) return;
            // Check reachability before making the anchor impassable. Avoid sealed mountain pockets.
            var cell = GenRadial.RadialCellsAround(map.Center, 60f, true).Concat(map.AllCells).First(c =>
                c.InBounds(map) && !c.CloseToEdge(map, 5) && c.Standable(map)
                && map.reachability.CanReachMapEdge(c, TraverseParms.For(TraverseMode.PassDoors)));
            anchor = (Building_CullingAnchor)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_CullingAnchor"));
            GenSpawn.Spawn(anchor, cell, map);
            foreach (var c in GenRadial.RadialCellsAround(cell, 4f, true).Where(c => c.InBounds(map) && c.Standable(map)))
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            generated = true;
            SpawnWave();
        }
        private void SpawnWave()
        {
            if (!HasMap || GameComponent_CullingGame.RivalFaction == null) return;
            IntVec3 edge;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out edge, Map, CellFinder.EdgeRoadChance_Animal))
                edge = CellFinder.RandomClosewalkCellNear(anchor.Position, Map, 20);
            var spawned = SpawnRivals(Map, edge, 5 + GameComponent_CullingGame.Instance.data.rivalPressure, finale || wave == WaveLimit - 1, true);
            if (spawned.Count == 0) return;
            rivals.AddRange(spawned); wave++; nextWaveTick = 0;
            Find.LetterStack.ReceiveLetter("JJK_Kogane".Translate(), "JJK_CullingWave".Translate(LabelCap, wave, WaveLimit),
                LetterDefOf.ThreatBig, spawned[0]);
        }
        public void UpdateEncounter(int now)
        {
            if (!CampaignActive || !generated || disrupted || !HasMap || Remaining > 0 || wave >= WaveLimit) return;
            // Waves wait for the expedition party. Leaving never resets the defenders or the wave count.
            if (!Map.mapPawns.FreeColonistsSpawned.Any()) return;
            if (nextWaveTick == 0) nextWaveTick = now + 2500;
            if (now >= nextWaveTick) SpawnWave();
        }
        public static List<Pawn> SpawnRivals(Map map, IntVec3 center, int count, bool elite, bool register)
        {
            var pawns = new List<Pawn>(); var faction = GameComponent_CullingGame.RivalFaction;
            if (faction == null) return pawns;
            for (int i = 0; i < count; i++)
            {
                string kind = elite && i == 0 ? "JJK_CullingIncarnated" : (i % 2 == 0 ? "JJK_CullingAwakened" : "JJK_CullingBrawler");
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind), faction,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(center, map, 8), map);
                pawns.Add(p);
                if (register) GameComponent_CullingGame.Instance.Register(p);
            }
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, pawns);
            return pawns;
        }
    }
    public class SitePartWorker_CullingColony : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        { base.PostMapGenerate(map); (map.Parent as CullingSite)?.Generate(map); }
    }
    public class Building_CullingAnchor : Building
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var opt in base.GetFloatMenuOptions(selPawn)) yield return opt;
            var site = Map?.Parent as CullingSite;
            if (site == null || !site.CampaignActive || site.disrupted || !GameComponent_CullingGame.LivingPlayer(selPawn)) yield break;
            if (!site.Secured) { yield return new FloatMenuOption("JJK_CullingSecureFirst".Translate(), null); yield break; }
            if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            { yield return new FloatMenuOption("NoPath".Translate(), null); yield break; }
            yield return new FloatMenuOption("JJK_CullingDisrupt".Translate(), () => selPawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_DisruptCullingAnchor"), this)));
        }
        public override string GetInspectString() => base.GetInspectString() + "\n" + (Map?.Parent as CullingSite)?.Progress;
    }
    public class JobDriver_DisruptCullingAnchor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !(pawn.Map?.Parent is CullingSite site) || !site.CampaignActive || !site.Secured || site.disrupted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => GameComponent_CullingGame.Instance.Disrupt(pawn.Map.Parent as CullingSite, pawn));
        }
    }
}
