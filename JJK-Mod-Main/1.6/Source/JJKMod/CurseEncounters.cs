using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public enum CurseEncounterKind { Ambush, Stalker, Miasma }
    public class CurseEncounterExtension : DefModExtension { public CurseEncounterKind kind; }
    public class CurseEncounter : IExposable
    {
        public CurseEncounterKind kind;
        public IntVec3 center;
        public int triggerAt, endsAt, nextPulse;
        public float points;
        public Pawn target, stalker;
        public Thing nexus;
        public bool manifested;
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref triggerAt, "triggerAt"); Scribe_Values.Look(ref endsAt, "endsAt");
            Scribe_Values.Look(ref nextPulse, "nextPulse"); Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref manifested, "manifested");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (target?.Destroyed == true) target = null;
                if (stalker?.Destroyed == true) stalker = null;
                if (nexus?.Destroyed == true) nexus = null;
            }
            Scribe_References.Look(ref target, "target"); Scribe_References.Look(ref stalker, "stalker"); Scribe_References.Look(ref nexus, "nexus");
        }
    }
    public class MapComponent_CurseEncounters : MapComponent
    {
        public CurseEncounter active;
        public int nextIncident;
        public const float MiasmaRadius = 6;
        public MapComponent_CurseEncounters(Map map) : base(map) { }
        public bool Available => map.IsPlayerHome && active == null && Find.TickManager.TicksGame >= nextIncident
            && map.mapPawns.FreeColonistsSpawned.Any(p => !p.Downed && !p.Dead);
        public bool Begin(CurseEncounterKind kind, float points)
        {
            if (!Available) return false;
            var people = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead && !p.Downed).ToList();
            Pawn target = people.OrderByDescending(p => people.Where(x => x != p).Select(x => x.Position.DistanceToSquared(p.Position)).DefaultIfEmpty(9999).Min()).First();
            var cells = GenRadial.RadialCellsAround(target.Position, 35, true).Where(c => c.InBounds(map) && c.Standable(map)
                && !c.Fogged(map) && !c.Roofed(map) && !map.areaManager.Home[c]
                && people.All(p => p.Position.DistanceToSquared(c) >= 324)
                && target.CanReach(c, PathEndMode.OnCell, Danger.Some)).ToList();
            if (cells.Count == 0) return false;
            IntVec3 center = cells.RandomElement(); int now = Find.TickManager.TicksGame;
            var encounter = new CurseEncounter { kind = kind, center = center, points = Mathf.Clamp(points, 100, 900), target = target,
                triggerAt = now + (kind == CurseEncounterKind.Stalker ? 1800 : 600), endsAt = now + 60000, nextPulse = now };
            if (kind == CurseEncounterKind.Stalker)
            {
                var pool = CursedSpiritUtil.CurseKinds(); if (pool.Count == 0) return false;
                var eligible = pool.Where(k => k.combatPower <= Mathf.Clamp(points * .35f, 70, 280)).OrderByDescending(k => k.combatPower).ToList();
                var chosen = eligible.FirstOrDefault() ?? pool.OrderBy(k => k.combatPower).First();
                var stalker = PawnGenerator.GeneratePawn(chosen);
                GenSpawn.Spawn(stalker, center, map);
                // SpawnSetup normally makes factionless curses permanent manhunters.
                // Recover while stalking; the explicit attack transition restores it.
                stalker.mindState?.mentalStateHandler?.CurState?.RecoverFromState();
                encounter.stalker = stalker;
                stalker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, center));
            }
            active = encounter; nextIncident = now + 120000;
            CombatPresentation.Play("Warning", map, center);
            Find.LetterStack.ReceiveLetter(("JJK_Encounter" + kind).Translate(), ("JJK_Encounter" + kind + "Desc").Translate(target.LabelShortCap),
                LetterDefOf.ThreatBig, new LookTargets(center, map));
            return true;
        }
        public static int Escorts(Pawn target) => target?.Spawned != true ? 0 : target.Map.mapPawns.FreeColonistsSpawned
            .Count(p => p != target && !p.Dead && !p.Downed && !p.InMentalState && p.Position.DistanceToSquared(target.Position) <= 36);
        public static bool MiasmaAffects(Pawn p, Thing nexus) => p?.Spawned == true && nexus?.Spawned == true && !p.Dead && p.Map == nexus.Map
            && !JJK_Utils.IsCursedSpirit(p) && p.TryGetComp<CompCursedCorpse>() == null
            && p.Position.DistanceToSquared(nexus.Position) <= MiasmaRadius * MiasmaRadius
            && GenSight.LineOfSight(nexus.Position, p.Position, p.Map);
        public void Pulse(CurseEncounter e)
        {
            if (e?.nexus?.Spawned != true) return;
            var def = DefDatabase<HediffDef>.GetNamed("JJK_CursedMiasmaExposure");
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(p => MiasmaAffects(p, e.nexus)).ToList())
            {
                var h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
                h.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 180;
            }
        }
        private void ReleaseStalker(CurseEncounter e, bool attack)
        {
            Pawn p = e.stalker;
            if (p?.Spawned == true && !p.Dead && p.Faction != Faction.OfPlayer)
            {
                if (attack) p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
                else if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Animal)
                    && p.CanReach(edge, PathEndMode.OnCell, Danger.Deadly))
                {
                    var job = JobMaker.MakeJob(JobDefOf.Goto, edge); job.exitMapOnArrival = true; p.jobs.TryTakeOrderedJob(job);
                    Messages.Message("JJK_StalkerRetreat".Translate(), p, MessageTypeDefOf.PositiveEvent, false);
                }
            }
            active = null;
        }
        public override void MapComponentTick()
        {
            var e = active; if (e == null) return;
            int now = Find.TickManager.TicksGame;
            if (e.kind == CurseEncounterKind.Ambush)
            {
                if (now >= e.triggerAt) { CursedSpiritUtil.SpawnPack(map, e.center, e.points, 8); CombatPresentation.Play("Spirit", map, e.center); active = null; }
                return;
            }
            if (e.kind == CurseEncounterKind.Miasma)
            {
                if (now < e.triggerAt) return;
                if (e.nexus == null)
                {
                    // A destroyed source must never return after saving and loading.
                    if (e.manifested || now >= e.endsAt || !e.center.Standable(map) || e.center.GetEdifice(map) != null)
                    { active = null; return; }
                    e.nexus = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_CursedNexus"));
                    GenSpawn.Spawn(e.nexus, e.center, map); e.nexus.SetFaction(Faction.OfAncientsHostile);
                    e.manifested = true;
                    CursedSpiritUtil.SpawnPack(map, e.center, e.points * .4f, 3);
                }
                if (e.nexus.Destroyed || !e.nexus.Spawned || now >= e.endsAt)
                { if (!e.nexus.Destroyed) e.nexus.Destroy(); active = null; return; }
                if (now >= e.nextPulse) { e.nextPulse = now + 120; Pulse(e); }
                return;
            }
            if (now % 60 != 0) return;
            TickStalker(e);
        }
        public void TickStalker(CurseEncounter e)
        {
            int now = Find.TickManager.TicksGame;
            var stalker = e.stalker;
            if (stalker == null || stalker.Dead || !stalker.Spawned || stalker.Map != map || stalker.Faction == Faction.OfPlayer) { active = null; return; }
            if (e.target == null || e.target.Dead || !e.target.Spawned || e.target.Map != map || e.target.Faction != Faction.OfPlayer
                || Escorts(e.target) >= 2 || now >= e.endsAt) { ReleaseStalker(e, false); return; }
            if (stalker.Downed) return;
            if (stalker.health.summaryHealth.SummaryHealthPercent < .98f || now >= e.triggerAt || stalker.Position.DistanceToSquared(e.target.Position) <= 36)
            { ReleaseStalker(e, true); return; }
            if (stalker.CurJob?.def == JobDefOf.Goto) return;
            var approach = GenRadial.RadialCellsAround(e.target.Position, 11, true)
                .Where(c => c.InBounds(map) && c.Standable(map) && c.DistanceToSquared(e.target.Position) >= 64
                    && stalker.CanReach(c, PathEndMode.OnCell, Danger.Some))
                .OrderBy(c => c.DistanceToSquared(stalker.Position)).DefaultIfEmpty(IntVec3.Invalid).First();
            if (approach.IsValid && approach != stalker.Position) stalker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, approach));
        }
        public override void MapComponentUpdate()
        {
            var e = active; if (e == null || Find.CurrentMap != map || !CombatPresentation.Overlays) return;
            if (e.kind == CurseEncounterKind.Stalker) return;
            var cells = GenRadial.RadialCellsAround(e.center, e.kind == CurseEncounterKind.Miasma ? MiasmaRadius : 6, true).Where(c => c.InBounds(map));
            GenDraw.DrawFieldEdges(CombatPresentation.VisibleCells(map, cells), new Color(.8f, .3f, 1));
        }
        public override void MapComponentOnGUI()
        {
            var e = active; if (e == null) return;
            if (e.kind == CurseEncounterKind.Stalker)
            {
                if (e.stalker?.Spawned == true) CombatPresentation.Label(map, e.stalker.Position, "JJK_StalkerLabel".Translate(CombatPresentation.Countdown(e.triggerAt)), Color.white, -25);
            }
            else CombatPresentation.Label(map, e.center, e.nexus?.Spawned == true ? "JJK_MiasmaLabel".Translate()
                : "JJK_EncounterForming".Translate(("JJK_Encounter" + e.kind).Translate(), CombatPresentation.Countdown(e.triggerAt)), Color.white);
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref active, "curseEncounter"); Scribe_Values.Look(ref nextIncident, "nextCurseEncounter");
        }
    }
    public class IncidentWorker_CurseEncounter : IncidentWorker
    {
        public override bool CanFireNowSub(IncidentParms parms) => base.CanFireNowSub(parms)
            && parms.target is Map map && map.GetComponent<MapComponent_CurseEncounters>().Available;
        public override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map map
            && map.GetComponent<MapComponent_CurseEncounters>().Begin(def.GetModExtension<CurseEncounterExtension>().kind, parms.points);
    }
}
