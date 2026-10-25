using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public enum SealRoute { Undecided, Defend, Transport }
    public class SealingData : IExposable
    {
        public Pawn enemy;
        public List<Building_SealingAnchor> anchors = new List<Building_SealingAnchor>();
        public SealedEnemyObject vessel;
        public SealRoute route;
        public int defendedTicks, waves, nextWave;
        public bool captured, delivered;
        public void ExposeData()
        {
            Scribe_References.Look(ref enemy, "enemy"); Scribe_References.Look(ref vessel, "vessel");
            Scribe_Collections.Look(ref anchors, "anchors", LookMode.Reference);
            Scribe_Values.Look(ref route, "route"); Scribe_Values.Look(ref defendedTicks, "defendedTicks");
            Scribe_Values.Look(ref waves, "waves"); Scribe_Values.Look(ref nextWave, "nextWave");
            Scribe_Values.Look(ref captured, "captured"); Scribe_Values.Look(ref delivered, "delivered");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) anchors = anchors ?? new List<Building_SealingAnchor>();
        }
    }
    public static class SealingCampaign
    {
        public static void Generate(JujutsuContract c, Map map, IntVec3 center)
        {
            var d = c.sealing ?? (c.sealing = new SealingData());
            foreach (var offset in new[] { new IntVec3(-6, 0, -4), new IntVec3(6, 0, -4), new IntVec3(0, 0, 6) })
            {
                var anchor = (Building_SealingAnchor)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_SealingAnchor"));
                GenSpawn.Spawn(anchor, CellFinder.RandomClosewalkCellNear(center + offset, map, 2), map);
                d.anchors.Add(anchor);
            }
            d.enemy = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Boss_Jogo_Kind"));
            d.enemy.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_SealOverwhelming"));
            GenSpawn.Spawn(d.enemy, center, map); c.threats.Add(d.enemy); c.siteGenerated = true;
            Find.LetterStack.ReceiveLetter(c.Title, "JJK_SealBrief".Translate(), LetterDefOf.ThreatBig, d.enemy);
        }
        public static bool Able(Pawn p) => ColonyLifeUtility.Available(p) && p.Faction == Faction.OfPlayer && JJK_GradeUtility.IsSorcerer(p);
        public static AbilityResource Energy(Pawn p) => p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
        public static bool CanSeal(JujutsuContract c, Pawn actor, Building_SealingAnchor at)
        {
            var d = c?.sealing;
            return ColonyMissions.Active(c) && d != null && !d.captured && Able(actor) && at?.Spawned == true && actor.Map == at.Map
                && d.anchors.Count == 3 && d.anchors.All(a => a?.Spawned == true && a.activated)
                && d.enemy?.Spawned == true && !d.enemy.Dead && d.enemy.HostileTo(Faction.OfPlayer) && d.enemy.Map == at.Map
                && d.enemy.Position.InHorDistOf(at.Position, 8) && GenSight.LineOfSight(at.Position, d.enemy.Position, at.Map)
                && at.Map.mapPawns.FreeColonistsSpawned.Count(p => Able(p) && p.Position.InHorDistOf(at.Position, 8)) >= 2
                && (Energy(actor)?.energy ?? 0) >= 40;
        }
        public static bool Seal(JujutsuContract c, Pawn actor, Building_SealingAnchor at)
        {
            if (!CanSeal(c, actor, at)) return false;
            var d = c.sealing;
            var obj = (SealedEnemyObject)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_SealedEnemy"));
            if (!GenPlace.TryPlaceThing(obj, at.Position, at.Map, ThingPlaceMode.Near)) { obj.Destroy(); return false; }
            // Suspend the actual pawn: no kill credit, boss drop, faction change or regenerated identity.
            at.Map.GetComponent<MapComponent_DomainManager>().CollapseFor(d.enemy, false);
            d.enemy.jobs?.StopAll(); d.enemy.DeSpawn();
            if (!obj.GetDirectlyHeldThings().TryAdd(d.enemy))
            { GenSpawn.Spawn(d.enemy, CellFinder.RandomClosewalkCellNear(at.Position, at.Map, 3), at.Map); obj.Destroy(); return false; }
            Energy(actor).energy -= 40; d.vessel = obj; d.captured = true; obj.SetForbidden(false);
            c.siteCleared = true;
            GameComponent_CampaignHistory.Instance?.Add(actor, "JJK_RecordSeal", d.enemy.LabelCap);
            Messages.Message("JJK_SealCaptured".Translate(), obj, MessageTypeDefOf.PositiveEvent);
            return true;
        }
        public static bool ChooseRoute(JujutsuContract c, SealRoute route)
        {
            var d = c?.sealing;
            if (!ColonyMissions.Active(c) || d == null || !d.captured || d.route != SealRoute.Undecided
                || (route != SealRoute.Defend && route != SealRoute.Transport)) return false;
            d.route = route; d.nextWave = Find.TickManager.TicksGame;
            return true;
        }
        public static bool ObjectiveMet(JujutsuContract c) => c.sealing?.delivered == true && c.map != null
            && c.participants.Any(p => ExorcismExpeditions.Returned(p, c));
        public static void Tick(JujutsuContract c)
        {
            var d = c.sealing;
            if (!c.siteGenerated || d == null) return;
            if (d.delivered) return;
            if ((!d.captured && (d.enemy == null || d.enemy.Dead || d.enemy.Destroyed || d.anchors.Any(a => a == null || a.Destroyed)))
                || (d.captured && (d.vessel == null || d.vessel.Destroyed || !d.vessel.ContainsEnemy)))
            { GameComponent_Jujutsu.Instance.Fail(c); return; }
            if (!d.captured || d.route == SealRoute.Undecided) return;
            Map map = d.vessel.MapHeld;
            bool present = map != null && map.mapPawns.FreeColonistsSpawned.Any(ColonyLifeUtility.Available);
            if (!present) return;
            if (d.route == SealRoute.Defend && map != c.site?.Map) return;
            if (d.route == SealRoute.Transport && map != c.map) return;
            int now = Find.TickManager.TicksGame;
            if (d.waves < 3 && now >= d.nextWave && RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Animal))
            {
                var pack = CursedSpiritUtil.SpawnPack(map, edge, 240 + d.waves * 120, 6);
                if (pack.Count > 0)
                {
                    c.threats.AddRange(pack); d.waves++; d.nextWave = now + 15000;
                    Find.LetterStack.ReceiveLetter(c.Title, "JJK_SealRaid".Translate(d.waves), LetterDefOf.ThreatBig, pack[0]);
                }
            }
            d.defendedTicks += 250;
            if (d.waves == 3 && c.Remaining <= 1 && d.defendedTicks >= (d.route == SealRoute.Defend ? 60000 : 30000))
            {
                // The sealed pawn is still alive; count guards separately instead of requiring its death.
                if (c.threats.Any(p => p != null && p != d.enemy && !p.Dead && !p.Destroyed && p.Faction != Faction.OfPlayer)) return;
                d.delivered = true; d.vessel.handedOver = true;
                Messages.Message("JJK_SealDelivered".Translate(), d.vessel, MessageTypeDefOf.PositiveEvent);
            }
        }
        public static string Progress(JujutsuContract c)
        {
            var d = c.sealing;
            if (!c.siteGenerated || d == null) return "JJK_SealBrief".Translate();
            if (d.delivered) return "JJK_SealDelivered".Translate();
            if (!d.captured) return "JJK_SealConditions".Translate(d.anchors.Count(a => a?.Spawned == true && a.activated));
            return "JJK_SealRouteProgress".Translate(("JJK_SealRoute" + d.route).Translate(), d.waves,
                (d.defendedTicks / 60000f).ToString("0.00"));
        }
        public static void Draw(Listing_Standard list, JujutsuContract c)
        {
            if (c.sealing == null) return;
            if (c.sealing.captured && c.sealing.route == SealRoute.Undecided)
            {
                list.Label("JJK_SealRouteHelp".Translate());
                if (list.ButtonText("JJK_SealRouteDefend".Translate())) ChooseRoute(c, SealRoute.Defend);
                if (list.ButtonText("JJK_SealRouteTransport".Translate())) ChooseRoute(c, SealRoute.Transport);
            }
        }
    }
    public class SealedEnemyObject : ThingWithComps, IThingHolder
    {
        private ThingOwner<Pawn> held;
        public bool handedOver;
        public SealedEnemyObject() { held = new ThingOwner<Pawn>(this); }
        public bool ContainsEnemy => held.Count == 1 && !held[0].Dead;
        public ThingOwner GetDirectlyHeldThings() => held;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, held);
        public override void ExposeData()
        { base.ExposeData(); Scribe_Deep.Look(ref held, "sealedEnemy", this); Scribe_Values.Look(ref handedOver, "handedOver"); }
        public override string GetInspectString() => base.GetInspectString() + "\n" + "JJK_SealContents".Translate(held.InnerListForReading.FirstOrDefault()?.LabelCap ?? "?");
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = MapHeld; IntVec3 cell = PositionHeld;
            // Destruction on a map breaks the prison. Off-map loss releases the preserved pawn to world storage.
            foreach (var p in held.InnerListForReading.ToList())
            {
                if (handedOver || map == null) { held.Remove(p); Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever); }
                else held.TryDrop(p, cell, map, ThingPlaceMode.Near, out _);
            }
            base.Destroy(mode);
        }
    }
    public class Building_SealingAnchor : Building
    {
        public bool activated;
        public JujutsuContract Contract => GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => c.sealing?.anchors.Contains(this) == true);
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref activated, "activated"); }
        public override string GetInspectString() => Contract == null ? base.GetInspectString() : SealingCampaign.Progress(Contract);
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn p)
        {
            foreach (var opt in base.GetFloatMenuOptions(p)) yield return opt;
            if (!ColonyMissions.Active(Contract) || Contract.sealing.captured || !SealingCampaign.Able(p)
                || !p.CanReserveAndReach(this, PathEndMode.Touch, Danger.Deadly)) yield break;
            if (!activated) yield return new FloatMenuOption("JJK_SealActivate".Translate(), () => p.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_ActivateSeal"), this)));
            else yield return new FloatMenuOption("JJK_SealRitual".Translate(), () => p.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_SealEnemy"), this)));
        }
    }
    public class JobDriver_ActivateSeal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !SealingCampaign.Able(pawn) || (SealingCampaign.Energy(pawn)?.energy ?? 0) < 20
                || !(TargetA.Thing is Building_SealingAnchor a) || !ColonyMissions.Active(a.Contract) || a.activated || a.Contract.sealing.captured);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => { ((Building_SealingAnchor)TargetA.Thing).activated = true; SealingCampaign.Energy(pawn).energy -= 20; });
        }
    }
    public class JobDriver_SealEnemy : JobDriver
    {
        private int started;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref started, "sealStarted"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var ritual = Toils_General.Wait(900, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            ritual.AddPreInitAction(() => started = Find.TickManager.TicksGame);
            ritual.AddPreTickAction(() =>
            {
                var a = (Building_SealingAnchor)TargetA.Thing;
                if (!SealingCampaign.CanSeal(a.Contract, pawn, a) || pawn.mindState.lastHarmTick >= started) EndJobWith(JobCondition.Incompletable);
            });
            yield return ritual;
            yield return Toils_General.Do(() => { var a = (Building_SealingAnchor)TargetA.Thing; SealingCampaign.Seal(a.Contract, pawn, a); });
        }
    }
}
