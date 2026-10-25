using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class ResidualTrace : IExposable
    {
        public int id, tick, castTick;
        public IntVec3 cell;
        public Pawn source;
        public AbilityTreeDef tree;
        public bool hostile, followed;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref castTick, "castTick");
            Scribe_Values.Look(ref cell, "cell"); Scribe_References.Look(ref source, "source");
            Scribe_Defs.Look(ref tree, "tree"); Scribe_Values.Look(ref hostile, "hostile"); Scribe_Values.Look(ref followed, "followed");
        }
    }
    public class MapComponent_ResidualEnergy : MapComponent
    {
        public List<ResidualTrace> traces = new List<ResidualTrace>();
        public int sequence, nextCamp;
        public MapComponent_ResidualEnergy(Map map) : base(map) { }
        public void Record(Pawn source, AbilityTreeDef tree, int castTick = -1)
        {
            if (source?.Spawned != true || source.Map != map) return;
            var last = traces.LastOrDefault(t => t.source == source);
            if (last != null && last.cell == source.Position && SorceryDevelopment.Now - last.tick < 120)
            {
                // Coalesce repeated samples, but preserve an actual new cast's
                // technique and movement-trail window. Movement alone never renews it.
                if (castTick < 0)
                {
                    last.tick = last.castTick = SorceryDevelopment.Now;
                    last.tree = tree;
                    last.hostile = source.HostileTo(Faction.OfPlayer);
                    last.followed = false;
                }
                return;
            }
            traces.Add(new ResidualTrace { id = ++sequence, cell = source.Position, source = source, tree = tree,
                tick = SorceryDevelopment.Now, castTick = castTick < 0 ? SorceryDevelopment.Now : castTick, hostile = source.HostileTo(Faction.OfPlayer) });
            if (traces.Count > 256) traces.RemoveAt(0);
        }
        public override void MapComponentTick()
        {
            if (SorceryDevelopment.Now % 120 != 0) return;
            traces.RemoveAll(t => SorceryDevelopment.Now - t.tick > 15000);
            foreach (var last in traces.GroupBy(t => t.source).Select(g => g.Last()).ToList())
                if (last.source?.Spawned == true && last.source.Map == map && SorceryDevelopment.Now - last.castTick < 2500
                    && last.cell != last.source.Position) Record(last.source, last.tree, last.castTick);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref traces, "residualTraces", LookMode.Deep);
            Scribe_Values.Look(ref sequence, "traceSequence"); Scribe_Values.Look(ref nextCamp, "nextCamp");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) traces = traces ?? new List<ResidualTrace>();
        }
        public static bool Skilled(Pawn p) => p != null && JJK_GradeUtility.IsSorcerer(p)
            && GameComponent_JJK.Instance.GetGrade(p) >= SorcererGrade.Grade2;
        public List<ResidualTrace> Nearby(Pawn p) => traces.Where(t => !t.followed && t.hostile
            && SorceryDevelopment.Now - t.tick <= 15000 && p.Position.DistanceToSquared(t.cell) <= 625)
            .GroupBy(t => t.source).Select(g => g.OrderBy(t => t.tick).First()).ToList();
        public void Scan(Pawn p)
        {
            var r = SorceryDevelopment.State(p);
            if (!SorceryDevelopment.CanAct(p) || !Skilled(p) || SorceryDevelopment.Now < r.nextTrack || !SorceryDevelopment.Spend(p, 5)) return;
            r.nextTrack = SorceryDevelopment.Now + 600;
            var options = new List<FloatMenuOption>();
            foreach (var trace in Nearby(p))
            {
                var captured = trace;
                options.Add(new FloatMenuOption("JJK_DeepTrace".Translate(trace.tree?.LabelCap ?? "JJK_DeepUnknown".Translate(),
                    (SorceryDevelopment.Now - trace.tick).ToStringTicksToPeriod()), () => Follow(p, captured)));
                FleckMaker.Static(trace.cell, map, FleckDefOf.PsycastAreaEffect, 1);
            }
            // Existing investigation sites hide their curse behind real clues. Detection
            // locates those clues without completing the investigation or weakening its curse.
            foreach (var c in GameComponent_Jujutsu.Instance.contracts.Where(c => c.site?.Map == map && c.colony != null && ColonyMissions.Active(c)))
                foreach (var node in c.colony.nodes.Where(n => n?.Spawned == true && n.kind == 0 && p.Position.DistanceToSquared(n.Position) <= 2500))
                {
                    var at = node;
                    options.Add(new FloatMenuOption("JJK_DeepHiddenTrail".Translate(), () => { CameraJumper.TryJump(at); Find.Selector.ClearSelection(); Find.Selector.Select(at); }));
                }
            if (options.Count == 0) Messages.Message("JJK_DeepNoTrace".Translate(), p, MessageTypeDefOf.NeutralEvent);
            else Find.WindowStack.Add(new FloatMenu(options));
        }
        public void Follow(Pawn p, ResidualTrace trace)
        {
            if (!SorceryDevelopment.CanAct(p) || p.Map != map || !Skilled(p) || !traces.Contains(trace)
                || trace.followed || !trace.hostile || SorceryDevelopment.Now - trace.tick > 15000) return;
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_FollowResidual"), trace.cell); job.count = trace.id;
            p.jobs.TryTakeOrderedJob(job);
        }
        public void Arrive(Pawn p, int id)
        {
            var trace = traces.FirstOrDefault(t => t.id == id && !t.followed);
            if (trace == null || !SorceryDevelopment.CanAct(p) || p.Map != map || !trace.hostile
                || SorceryDevelopment.Now - trace.tick > 15000 || p.Position.DistanceToSquared(trace.cell) > 4 || !Skilled(p)) return;
            trace.followed = true;
            var next = traces.FirstOrDefault(t => t.source == trace.source && t.id > trace.id && !t.followed
                && t.hostile && SorceryDevelopment.Now - t.tick <= 15000);
            if (next != null) { Follow(p, next); return; }
            if (trace.source?.Spawned == true && trace.source.Map == map)
            { CameraJumper.TryJump(trace.cell, map); Messages.Message("JJK_DeepTrailEnd".Translate(), new TargetInfo(trace.cell, map), MessageTypeDefOf.NeutralEvent); return; }
            if (!trace.hostile || trace.source == null || trace.source.Dead || SorceryDevelopment.Now < nextCamp) return;
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, map.Tile, 2, 6, selectLandmarkChance: 0f)) return;
            var site = SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("JJK_ResidualCamp"), tile, null, false, 200);
            site.sitePartsKnown = true; Find.WorldObjects.Add(site);
            site.GetComponent<TimeoutComp>()?.StartTimeout(300000);
            nextCamp = SorceryDevelopment.Now + 300000;
            // Consume this signature so retracing an older sample cannot duplicate a camp.
            foreach (var sample in traces.Where(t => t.source == trace.source)) sample.followed = true;
            Find.LetterStack.ReceiveLetter("JJK_DeepCamp".Translate(), "JJK_DeepCampText".Translate(), LetterDefOf.ThreatSmall, site);
        }
    }
    public class JobDriver_FollowResidual : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !Map.GetComponent<MapComponent_ResidualEnergy>().traces.Any(t => t.id == job.count && !t.followed));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(120).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => Map.GetComponent<MapComponent_ResidualEnergy>().Arrive(pawn, job.count));
        }
    }
    public class SitePartWorker_ResidualCamp : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            var faction = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.defeated && f.HostileTo(Faction.OfPlayer) && f.def.defName.StartsWith("JJK_"));
            var kind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k.race?.GetModExtension<CursedSpiritExtension>() != null && k.combatPower >= 40 && k.combatPower <= 120);
            if (kind == null) kind = PawnKindDefOf.Pirate;
            for (int i = 0; i < 3; i++)
            {
                var p = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
            }
        }
    }
}
