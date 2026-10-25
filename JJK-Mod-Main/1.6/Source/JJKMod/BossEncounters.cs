using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public enum BossPattern { Eruption, Roots, Tide, SoulTouch }
    public class CompProperties_BossPattern : CompProperties
    {
        public BossPattern pattern;
        public int windup = 120, recovery = 120, interval = 900;
        public float range = 18f, radius = 3f, damage = 30f;
        public CompProperties_BossPattern() { compClass = typeof(CompBossPattern); }
    }
    public class CompBossPattern : ThingComp
    {
        public CompProperties_BossPattern Props => (CompProperties_BossPattern)props;
        public IntVec3 aim = IntVec3.Invalid, origin = IntVec3.Invalid;
        public Pawn victim;
        public int resolveAt, recoveryUntil, nextAt;
        public bool Charging => resolveAt > 0;
        public bool Busy => Charging || Find.TickManager.TicksGame < recoveryUntil;
        private Pawn Boss => parent as Pawn;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Boss?.Map?.GetComponent<MapComponent_BossTelegraphs>()?.Register(this);
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.GetComponent<MapComponent_BossTelegraphs>()?.Unregister(this);
            base.PostDeSpawn(map, mode);
        }
        public List<IntVec3> DangerCells()
        {
            Map map = Boss.Map;
            if (map == null || !aim.IsValid) return new List<IntVec3>();
            if (Props.pattern == BossPattern.SoulTouch)
                return GenRadial.RadialCellsAround(Boss.Position, 2f, true).Where(c => c.InBounds(map)).ToList();
            if (Props.pattern != BossPattern.Tide)
                return GenRadial.RadialCellsAround(aim, Props.radius, true).Where(c => c.InBounds(map)).ToList();
            var dir = (aim - origin).ToVector3(); dir.y = 0;
            if (dir.sqrMagnitude < 0.1f) dir = Boss.Rotation.FacingCell.ToVector3();
            dir.Normalize();
            return GenRadial.RadialCellsAround(origin, 14f, true).Where(c => c.InBounds(map)).Where(c =>
            {
                var delta = (c - origin).ToVector3(); float forward = Vector3.Dot(delta, dir);
                return forward >= 0 && forward <= 14 && Mathf.Abs(delta.x * dir.z - delta.z * dir.x) <= 2;
            }).ToList();
        }
        public bool Begin(Pawn target)
        {
            if (Boss == null || !Boss.Spawned || Boss.Downed || Busy || target == null || !target.Spawned
                || target.Map != Boss.Map || !target.HostileTo(Boss) || target.Dead
                || target.Position.DistanceToSquared(Boss.Position) > Props.range * Props.range) return false;
            victim = target; aim = target.Position; origin = Boss.Position;
            resolveAt = Find.TickManager.TicksGame + Props.windup;
            CombatPresentation.Play("Warning", Boss.Map, Boss.Position);
            Boss.pather.StopDead(); Boss.jobs.StopAll();
            Boss.stances.stunner.StunFor(Props.windup, Boss, false, false);
            Messages.Message(("JJK_BossWarn" + Props.pattern).Translate(Boss.LabelShortCap),
                new LookTargets(aim, Boss.Map), MessageTypeDefOf.ThreatSmall, false);
            return true;
        }
        public void Resolve()
        {
            if (!Charging || Boss == null || !Boss.Spawned) return;
            resolveAt = 0;
            int now = Find.TickManager.TicksGame;
            nextAt = now + Props.interval; recoveryUntil = now + Props.recovery;
            if (Boss.Dead || Boss.Downed) { victim = null; return; }
            CombatPresentation.Play("Impact", Boss.Map, aim);
            using var exposure = new MahoragaExposure(Props.pattern == BossPattern.Eruption ? "JJK_Adapt_Flames"
                : Props.pattern == BossPattern.Roots ? "JJK_Adapt_Roots" : Props.pattern == BossPattern.Tide ? "JJK_Adapt_Tides" : "JJK_Adapt_Soul", Boss);
            var cells = new HashSet<IntVec3>(DangerCells());
            if (Props.pattern == BossPattern.SoulTouch)
            {
                if (victim != null && victim.Spawned && !victim.Dead && victim.Map == Boss.Map && victim.HostileTo(Boss)
                    && victim.Position.DistanceToSquared(Boss.Position) <= 4
                    && GenSight.LineOfSight(Boss.Position, victim.Position, Boss.Map))
                {
                    if (victim.RaceProps.Humanlike && victim.story?.traits?.HasTrait(JJK_DefOf.JJK_SoulObserver) != true)
                        TransfiguredSoulUtil.Transfigure(Boss, victim);
                    else victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, 10f, instigator: Boss));
                }
            }
            else foreach (Pawn p in Boss.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || !p.HostileTo(Boss) || !cells.Contains(p.Position)
                    || !GenSight.LineOfSight(Props.pattern == BossPattern.Tide ? origin : aim, p.Position, Boss.Map)) continue;
                DamageDef damage = Props.pattern == BossPattern.Eruption ? DamageDefOf.Burn
                    : Props.pattern == BossPattern.Roots ? DamageDefOf.Cut : DamageDefOf.Blunt;
                p.TakeDamage(new DamageInfo(damage, Props.damage, 0.3f, instigator: Boss));
                if (!p.Dead && Props.pattern == BossPattern.Roots) p.stances.stunner.StunFor(120, Boss, true, true);
            }
            FleckMaker.ThrowSmoke(aim.ToVector3Shifted(), Boss.Map, Props.radius);
            Boss.stances.stunner.StunFor(Props.recovery, Boss, false, false);
            var opening = Boss.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_BossOpening"));
            opening.TryGetComp<HediffComp_Disappears>().ticksToDisappear = Props.recovery;
            victim = null;
        }
        public override void CompTick()
        {
            if (Boss == null || !Boss.Spawned || Boss.Dead) return;
            if (Boss.Downed) { resolveAt = 0; victim = null; return; }
            int now = Find.TickManager.TicksGame;
            if (Charging) { if (now >= resolveAt) Resolve(); return; }
            if (Busy || now < nextAt || !Boss.IsHashIntervalTick(60)) return;
            Pawn target = Boss.Map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(Boss)
                && p.Position.DistanceToSquared(Boss.Position) <= Props.range * Props.range
                && GenSight.LineOfSight(Boss.Position, p.Position, Boss.Map)).OrderBy(p => p.Position.DistanceToSquared(Boss.Position)).FirstOrDefault();
            Begin(target);
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref aim, "patternAim"); Scribe_Values.Look(ref origin, "patternOrigin");
            if (Scribe.mode == LoadSaveMode.Saving && victim?.Destroyed == true) victim = null;
            Scribe_References.Look(ref victim, "patternVictim");
            Scribe_Values.Look(ref resolveAt, "patternResolveAt"); Scribe_Values.Look(ref nextAt, "patternNextAt");
            Scribe_Values.Look(ref recoveryUntil, "patternRecoveryUntil");
        }
        public override string CompInspectStringExtra() => Charging
            ? "JJK_BossCharging".Translate((resolveAt - Find.TickManager.TicksGame).ToStringTicksToPeriod())
            : Busy ? "JJK_BossOpening".Translate() : null;
    }
    public class MapComponent_BossTelegraphs : MapComponent
    {
        private readonly List<CompBossPattern> bosses = new List<CompBossPattern>();
        private bool initialized;
        public MapComponent_BossTelegraphs(Map map) : base(map) { }
        internal void Register(CompBossPattern pattern) { if (!bosses.Contains(pattern)) bosses.Add(pattern); }
        internal void Unregister(CompBossPattern pattern) => bosses.Remove(pattern);
        internal IReadOnlyList<CompBossPattern> Bosses
        {
            get
            {
                if (!initialized)
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        var pattern = pawn.TryGetComp<CompBossPattern>();
                        if (pattern != null) Register(pattern);
                    }
                    initialized = true;
                }
                return bosses;
            }
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays) return;
            foreach (var pattern in Bosses)
            {
                var p = (Pawn)pattern.parent;
                if (p.Spawned && p.Map == map && !p.Dead && !p.Downed && pattern.Charging && !p.Position.Fogged(map))
                    CombatPresentation.Danger(map, pattern.DangerCells());
            }
        }
        public override void MapComponentOnGUI()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays) return;
            foreach (var pattern in Bosses)
            {
                var p = (Pawn)pattern.parent;
                if (!p.Spawned || p.Map != map || p.Dead || p.Downed || !CombatPresentation.Visible(map, p.Position)) continue;
                if (pattern.Charging)
                    CombatPresentation.Label(map, pattern.Props.pattern == BossPattern.SoulTouch ? p.Position : pattern.aim,
                        "JJK_OverlayBoss".Translate(("JJK_Pattern" + pattern.Props.pattern).Translate(), CombatPresentation.Countdown(pattern.resolveAt)), Color.white);
                else if (pattern.Busy)
                    CombatPresentation.Label(map, p.Position, "JJK_OverlayRecovery".Translate(CombatPresentation.Countdown(pattern.recoveryUntil)), Color.green, -25);
            }
        }
    }
}
