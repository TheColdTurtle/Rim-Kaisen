using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public enum HauntedRule { Hospital, Shrine, Tunnel, School }
    public class ColonyMissionData : IExposable
    {
        public int clues, nextWave, extracted;
        public bool revealed, broken;
        public HauntedRule rule;
        public IntVec3 center, extraction;
        public List<Building_MissionClue> nodes = new List<Building_MissionClue>();
        public List<Pawn> civilians = new List<Pawn>();
        public List<Thing> keystones = new List<Thing>();
        public Thing offering;
        public int Found => ((clues & 1) != 0 ? 1 : 0) + ((clues & 2) != 0 ? 1 : 0) + ((clues & 4) != 0 ? 1 : 0);
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                for (int i = 0; i < keystones.Count; i++) if (keystones[i]?.Destroyed == true) keystones[i] = null;
                for (int i = 0; i < civilians.Count; i++) if (civilians[i]?.Destroyed == true) civilians[i] = null;
                if (offering?.Destroyed == true) offering = null;
            }
            Scribe_Values.Look(ref clues, "clues"); Scribe_Values.Look(ref nextWave, "nextWave");
            Scribe_Values.Look(ref extracted, "extracted"); Scribe_Values.Look(ref revealed, "revealed");
            Scribe_Values.Look(ref broken, "broken"); Scribe_Values.Look(ref rule, "rule");
            Scribe_Values.Look(ref center, "center"); Scribe_Values.Look(ref extraction, "extraction");
            Scribe_Collections.Look(ref nodes, "nodes", LookMode.Reference);
            Scribe_Collections.Look(ref civilians, "civilians", LookMode.Reference);
            Scribe_Collections.Look(ref keystones, "keystones", LookMode.Reference);
            Scribe_References.Look(ref offering, "offering");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nodes = nodes ?? new List<Building_MissionClue>(); civilians = civilians ?? new List<Pawn>();
                keystones = keystones ?? new List<Thing>();
            }
        }
        public string RuleText => rule == HauntedRule.Hospital ? "Hospital: tend every bleeding or untended wound on the survivor, then perform the ward ritual."
            : rule == HauntedRule.Shrine ? "Shrine: return the stolen offering to the ward. Carry it with the Return offering order, then perform the ritual."
            : rule == HauntedRule.Tunnel ? "Tunnel: destroy both marked keystones, then perform the ward ritual. Killing curses alone cannot free the tunnel."
            : "School: two undrafted colonists must remain within 8 cells of the ward during the entire silent vigil. Taking damage interrupts the ritual.";
    }
    public static class ColonyMissions
    {
        public static bool Special(ContractKind k) => k == ContractKind.Investigation || k == ContractKind.Evacuation || k == ContractKind.Haunting;
        public static bool Active(JujutsuContract c) => c != null && (c.state == ContractState.Active || c.state == ContractState.Ready);
        public static JujutsuContract For(Thing t) => GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => c.colony != null
            && c.site?.Map == t.MapHeld && c.colony.nodes.Contains(t as Building_MissionClue));
        public static Pawn Civilian(Map map, IntVec3 at, Faction faction)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            // Ordinary survivors are not a source of guaranteed techniques or equipment.
            if (p.genes != null) foreach (var g in p.genes.GenesListForReading.Where(g => g is Gene_CursedTechnique || g is Gene_CursedEnergyReserve).ToList()) p.genes.RemoveGene(g);
            p.equipment?.DestroyAllEquipment();
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(at, map, 3), map);
            var wait = JobMaker.MakeJob(JobDefOf.Wait, p.Position); wait.expiryInterval = 1200000;
            p.jobs.TryTakeOrderedJob(wait);
            return p;
        }
        public static Building_MissionClue Node(JujutsuContract c, Map map, IntVec3 at, int kind)
        {
            var node = (Building_MissionClue)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(kind == 3 ? "JJK_HauntedWard" : kind == 4 ? "JJK_ExtractionBeacon" : "JJK_InvestigationClue"));
            node.kind = kind; GenSpawn.Spawn(node, CellFinder.RandomClosewalkCellNear(at, map, 3), map);
            c.colony.nodes.Add(node); return node;
        }
        public static void Generate(JujutsuContract c, Map map, IntVec3 center)
        {
            var d = c.colony ?? (c.colony = new ColonyMissionData { rule = (HauntedRule)Rand.Range(0, 4) });
            d.center = center;
            BuildHauntedLocation(c, map, center);
            if (!RCellFinder.TryFindRandomPawnEntryCell(out d.extraction, map, CellFinder.EdgeRoadChance_Animal)) d.extraction = center;
            if (c.kind == ContractKind.Evacuation)
            {
                Node(c, map, d.extraction, 4);
                for (int i = 0; i < 3; i++)
                {
                    var p = Civilian(map, center + new IntVec3(i * 4, 0, 0), c.school);
                    d.civilians.Add(p);
                    if (i == 0) p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 8));
                }
                Reveal(c); d.nextWave = Find.TickManager.TicksGame + 15000;
            }
            else
            {
                Node(c, map, center + new IntVec3(-10, 0, 0), 0);
                var witness = Node(c, map, center + new IntVec3(0, 0, 7), 1);
                d.civilians.Add(Civilian(map, witness.Position, c.school));
                Node(c, map, center + new IntVec3(10, 0, 0), 2);
                Node(c, map, center, 3);
                if (c.kind == ContractKind.Haunting)
                {
                    if (d.rule == HauntedRule.Hospital) d.civilians[0].TakeDamage(new DamageInfo(DamageDefOf.Cut, 8));
                    if (d.rule == HauntedRule.Shrine)
                    {
                        d.offering = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_StolenOffering"));
                        GenPlace.TryPlaceThing(d.offering, center + new IntVec3(12, 0, 0), map, ThingPlaceMode.Near);
                        d.offering.SetForbidden(false);
                    }
                    if (d.rule == HauntedRule.Tunnel) foreach (int offset in new[] { -7, 7 })
                    {
                        var stone = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_HauntedKeystone"));
                        GenSpawn.Spawn(stone, CellFinder.RandomClosewalkCellNear(center + new IntVec3(offset, 0, 0), map, 2), map);
                        d.keystones.Add(stone);
                    }
                }
            }
            c.siteGenerated = true;
        }
        private static void BuildHauntedLocation(JujutsuContract c, Map map, IntVec3 center)
        {
            var rule = c.colony.rule;
            CellRect rect = CellRect.CenteredOn(center, rule == HauntedRule.Tunnel ? 17 : 8, rule == HauntedRule.Tunnel ? 3 : 8);
            foreach (var cell in rect.Cells.Where(p => p.InBounds(map) && p.Standable(map)).ToList())
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                bool entrance = rule == HauntedRule.Tunnel ? cell.z == center.z : cell.x == center.x;
                if (rect.EdgeCells.Contains(cell) && !entrance && cell.GetEdifice(map) == null)
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, DefDatabase<ThingDef>.GetNamed("BlocksSandstone")), cell, map);
            }
            if (rule == HauntedRule.Hospital) foreach (int offset in new[] { -4, 4 })
                Furnish("Bed", center + new IntVec3(offset, 0, 3), map);
            if (rule == HauntedRule.School) foreach (int offset in new[] { -4, 4 })
                Furnish("JJK_SchoolDesk", center + new IntVec3(offset, 0, 3), map);
            if (rule == HauntedRule.Shrine) Furnish("SculptureSmall", center + new IntVec3(0, 0, 3), map);
            c.site.customLabel = c.Title + (c.kind == ContractKind.Haunting ? " — haunted " + rule.ToString().ToLowerInvariant() : "");
        }
        private static void Furnish(string defName, IntVec3 cell, Map map)
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName);
            if (!GenAdj.OccupiedRect(cell, Rot4.North, def.size).Cells.All(p => p.InBounds(map) && p.Standable(map) && p.GetEdifice(map) == null)) return;
            GenSpawn.Spawn(ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null), cell, map);
        }
        public static void Reveal(JujutsuContract c)
        {
            var d = c?.colony; Map map = c?.site?.Map;
            if (d == null || map == null || d.revealed || !Active(c)) return;
            d.revealed = true;
            bool prepared = d.Found == 3;
            if (c.campaign?.stage == SchoolStage.Investigation) c.campaign.prepared = prepared;
            c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, d.center, 180f * c.tier * (prepared ? 0.8f : 1.4f), 10));
            if (prepared) foreach (var p in c.threats) p.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_ExposedCurse"));
            else if (c.kind != ContractKind.Evacuation)
            {
                var target = map.mapPawns.FreeColonistsSpawned.RandomElementWithFallback();
                if (target != null) c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, target.Position, 80f * c.tier, 3));
            }
            d.nextWave = Find.TickManager.TicksGame + 12000;
            Find.LetterStack.ReceiveLetter("Curse revealed", c.kind == ContractKind.Evacuation
                ? "Protect the civilians. Select a colonist and right-click a survivor to escort them; carry downed survivors to the extraction beacon. At least two of three must leave alive."
                : prepared ? "The evidence exposed the curse's weakness and prevented an ambush. " + (c.kind == ContractKind.Haunting ? d.RuleText : "Defeat the revealed enemy and return home.")
                : "The incomplete investigation triggered an ambush!", LetterDefOf.ThreatSmall, new LookTargets(d.center, map));
        }
        public static string Progress(JujutsuContract c)
        {
            var d = c.colony;
            if (d == null || !c.siteGenerated) return "Travel to the site to begin.";
            if (c.kind == ContractKind.Evacuation) return "Civilians extracted alive: " + d.extracted + "/3 (2 required). Bring a participating colonist home to claim.";
            return "Evidence: " + d.Found + "/3. " + (!d.revealed ? "Inspect residual energy, interview the survivor, and inspect the scene."
                : c.kind == ContractKind.Haunting ? (d.broken ? "Curse condition broken. Return home." : d.RuleText) : "Curse weakness discovered. Defeat the enemy, then return home.");
        }
        public static bool SiteComplete(JujutsuContract c) => c.colony != null && (c.kind == ContractKind.Evacuation
            ? c.colony.extracted >= 2 : c.kind == ContractKind.Haunting ? c.colony.broken : c.colony.revealed && c.Remaining == 0);
        public static bool RuleSatisfied(JujutsuContract c, Pawn actor)
        {
            var d = c.colony; Map map = c.site?.Map;
            if (d == null || !d.revealed || d.Found != 3 || map == null) return false;
            var ward = d.nodes.FirstOrDefault(n => n?.Spawned == true && n.kind == 3);
            if (ward == null) return false;
            switch (d.rule)
            {
                case HauntedRule.Hospital: return d.civilians.Count > 0 && d.civilians.All(p => p != null && !p.Dead && !p.Destroyed
                    && !p.health.hediffSet.hediffs.Any(h => h.TendableNow()) && p.health.hediffSet.BleedRateTotal <= 0);
                case HauntedRule.Shrine: return d.offering?.Spawned == true && d.offering.Map == map && d.offering.Position.DistanceToSquared(ward.Position) <= 9;
                case HauntedRule.Tunnel: return d.keystones.Count == 2 && d.keystones.All(t => t == null || t.Destroyed);
                default: return map.mapPawns.FreeColonistsSpawned.Count(p => ColonyLifeUtility.Available(p) && !p.Drafted
                    && p.Position.DistanceToSquared(ward.Position) <= 64) >= 2 && actor?.Drafted == false;
            }
        }
        public static void Tick(JujutsuContract c)
        {
            var d = c.colony;
            if (d == null || !c.siteGenerated || !Active(c)) return;
            if (c.kind == ContractKind.Evacuation && d.extracted + d.civilians.Count(p => p != null && !p.Dead && !p.Destroyed) < 2)
            { GameComponent_Jujutsu.Instance.Fail(c); return; }
            if (c.kind == ContractKind.Haunting && !d.broken && d.rule == HauntedRule.Hospital && d.civilians.Any(p => p == null || p.Dead || p.Destroyed))
            { GameComponent_Jujutsu.Instance.Fail(c); return; }
            if (c.kind == ContractKind.Haunting && (!d.broken && d.rule == HauntedRule.Shrine && (d.offering == null || d.offering.Destroyed)
                || (d.clues & 2) == 0 && d.civilians.All(p => p == null || p.Dead || p.Destroyed)))
            { GameComponent_Jujutsu.Instance.Fail(c); return; }
            Map map = c.site?.Map;
            if (map == null || !map.mapPawns.FreeColonistsSpawned.Any() || !d.revealed || SiteComplete(c)) return;
            if (Find.TickManager.TicksGame >= d.nextWave && (c.kind == ContractKind.Evacuation || c.kind == ContractKind.Haunting))
            {
                d.nextWave = Find.TickManager.TicksGame + 15000;
                if (c.Remaining < 8) c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, d.center, 80f * c.tier, 2));
            }
        }
        public static bool Extract(JujutsuContract c, Pawn civilian, Pawn escort)
        {
            var beacon = c.colony.nodes.FirstOrDefault(n => n?.Spawned == true && n.kind == 4);
            if (!Active(c) || beacon == null || !ColonyLifeUtility.Available(escort) || escort.Map != beacon.Map
                || civilian == null || !c.colony.civilians.Contains(civilian) || civilian.Dead || civilian.Destroyed || civilian.MapHeld != beacon.Map
                || civilian.PositionHeld.DistanceToSquared(beacon.Position) > 16 || escort.Position.DistanceToSquared(beacon.Position) > 16) return false;
            c.colony.civilians.Remove(civilian); c.colony.extracted++;
            if (civilian.Spawned) civilian.DeSpawn();
            else civilian.holdingOwner?.Remove(civilian);
            Find.WorldPawns.PassToWorld(civilian, PawnDiscardDecideMode.KeepForever);
            Messages.Message(civilian.LabelShortCap + " extracted safely (" + c.colony.extracted + "/3).", MessageTypeDefOf.PositiveEvent);
            return true;
        }
    }
    public class Building_MissionClue : Building
    {
        public int kind;
        public JujutsuContract Contract => ColonyMissions.For(this);
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref kind, "clueKind"); }
        public override string GetInspectString() => (Contract == null ? "Inactive assignment" : ColonyMissions.Progress(Contract))
            + "\n" + (kind == 0 ? "Residual energy trail" : kind == 1 ? "Survivor interview point" : kind == 2 ? "Haunted scene" : kind == 3 ? "Curse ward" : "Civilian extraction point");
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            var c = Contract;
            if (kind == 3 && ColonyMissions.Active(c) && !c.colony.revealed)
                yield return ColonyLifeUtility.Command("Confront the curse early", "Requires a colonist at the ward. Incomplete evidence risks an ambush and loses the weakness advantage.", () =>
                {
                    if (Map.mapPawns.FreeColonistsSpawned.Any(p => ColonyLifeUtility.Available(p) && p.Position.DistanceToSquared(Position) <= 9)) ColonyMissions.Reveal(c);
                    else ColonyLifeUtility.Reject("Bring a colonist within 3 cells of the ward.");
                });
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn p)
        {
            foreach (var o in base.GetFloatMenuOptions(p)) yield return o;
            var c = Contract;
            if (!ColonyMissions.Active(c) || !ColonyLifeUtility.Available(p) || !p.CanReserveAndReach(this, PathEndMode.Touch, Danger.Deadly)) yield break;
            if (kind < 3 && (c.colony.clues & (1 << kind)) == 0)
                yield return new FloatMenuOption(kind == 0 ? "Follow residual energy" : kind == 1 ? "Interview survivor" : "Inspect haunted scene",
                    () => p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_InvestigateClue"), this)));
            if (kind == 3 && c.kind == ContractKind.Haunting && !c.colony.broken)
            {
                if (ColonyMissions.RuleSatisfied(c, p)) yield return new FloatMenuOption("Break the curse's condition (silent ritual)",
                    () => p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_BreakHaunting"), this)));
                else yield return new FloatMenuOption(c.colony.Found < 3 ? "Find all three clues first" : c.colony.RuleText, null);
                if (c.colony.Found == 3 && c.colony.rule == HauntedRule.Shrine && c.colony.offering?.Spawned == true
                    && p.CanReserveAndReach(c.colony.offering, PathEndMode.Touch, Danger.Deadly))
                    yield return new FloatMenuOption("Return stolen offering", () =>
                    {
                        var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_ReturnOffering"), c.colony.offering, this);
                        job.count = 1; p.jobs.TryTakeOrderedJob(job);
                    });
            }
            if (kind == 4)
            {
                Pawn carried = p.carryTracker.CarriedThing as Pawn;
                if (carried != null && c.colony.civilians.Contains(carried)) yield return new FloatMenuOption("Extract carried civilian",
                    () => p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_ExtractCarried"), this)));
                foreach (var civilian in c.colony.civilians.Where(v => v?.Spawned == true && !v.Dead && v.Position.DistanceToSquared(Position) <= 16).ToList())
                    yield return new FloatMenuOption("Extract " + civilian.LabelShortCap, () => p.jobs.TryTakeOrderedJob(
                        JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_ExtractCarried"), this, civilian)));
            }
        }
    }
    public class JobDriver_InvestigateClue : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !ColonyMissions.Active((TargetA.Thing as Building_MissionClue)?.Contract));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                var n = (Building_MissionClue)TargetA.Thing; var c = n.Contract;
                if (n.kind == 1 && !c.colony.civilians.Any(p => p?.Spawned == true && !p.Dead && !p.Downed && p.Position.DistanceToSquared(n.Position) <= 64))
                { ColonyLifeUtility.Reject("The witness must be alive, conscious, and near the interview point."); return; }
                c.colony.clues |= 1 << n.kind;
                pawn.skills?.Learn(SkillDefOf.Intellectual, 100);
                Messages.Message(n.kind == 0 ? "Residual traces pinpoint the ward." : n.kind == 1 ? "The survivor describes the curse's pattern." : "The scene reveals the curse's binding condition.", n, MessageTypeDefOf.PositiveEvent);
                if (c.colony.Found == 3) ColonyMissions.Reveal(c);
            });
        }
    }
    public class JobDriver_BreakHaunting : JobDriver
    {
        private int started;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref started, "vigilStarted"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !ColonyMissions.Active((TargetA.Thing as Building_MissionClue)?.Contract)
                || !ColonyMissions.RuleSatisfied(((Building_MissionClue)TargetA.Thing).Contract, pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var vigil = Toils_General.Wait(1200, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            vigil.AddPreInitAction(() => started = Find.TickManager.TicksGame);
            vigil.AddPreTickAction(() => { if (pawn.mindState.lastHarmTick >= started) EndJobWith(JobCondition.Incompletable); });
            yield return vigil;
            yield return Toils_General.Do(() =>
            {
                var c = ((Building_MissionClue)TargetA.Thing).Contract; c.colony.broken = true;
                if (c.colony.offering != null && !c.colony.offering.Destroyed) c.colony.offering.Destroy();
                foreach (var curse in c.threats.Where(p => p != null && !p.Dead && !p.Destroyed).ToList()) curse.Kill(null);
                var relic = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_DangerousRelic"));
                GenPlace.TryPlaceThing(relic, pawn.Position, pawn.Map, ThingPlaceMode.Near); relic.SetForbidden(false);
                Messages.Message("The binding condition breaks. The haunting ends, leaving a dangerous relic to contain.", pawn, MessageTypeDefOf.PositiveEvent);
            });
        }
    }
    public class JobDriver_ReturnOffering : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed) && pawn.Reserve(TargetB, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A); this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => !ColonyMissions.Active((TargetB.Thing as Building_MissionClue)?.Contract));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Do(() => pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _));
        }
    }
    public class JobDriver_ExtractCarried : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => ColonyMissions.Extract(((Building_MissionClue)TargetA.Thing).Contract,
                TargetB.Pawn ?? pawn.carryTracker.CarriedThing as Pawn, pawn));
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(Pawn), nameof(Pawn.GetFloatMenuOptions))]
    public static class Patch_CivilianEscortMenu
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, Pawn __instance, Pawn selPawn)
        {
            foreach (var v in values) yield return v;
            var c = GameComponent_Jujutsu.Instance?.contracts.FirstOrDefault(c => ColonyMissions.Active(c) && c.kind == ContractKind.Evacuation
                && c.colony?.civilians.Contains(__instance) == true);
            if (c == null || !ColonyLifeUtility.Available(selPawn) || !__instance.Spawned || __instance.Dead
                || !selPawn.CanReserveAndReach(__instance, PathEndMode.Touch, Danger.Deadly)) yield break;
            yield return new FloatMenuOption(__instance.Downed ? "Carry civilian to extraction" : "Escort civilian to extraction", () => selPawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_EscortCivilian"), __instance, c.colony.nodes.First(n => n.kind == 4))));
        }
    }
    public class JobDriver_EscortCivilian : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A); this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => TargetA.Pawn.Dead || !ColonyMissions.Active(((Building_MissionClue)TargetB.Thing).Contract));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var follow = Toils_General.Do(() =>
            {
                if (!TargetA.Pawn.Downed)
                {
                    var followJob = JobMaker.MakeJob(JobDefOf.Follow, pawn); followJob.expiryInterval = 30000;
                    followJob.followRadius = 2;
                    TargetA.Pawn.jobs.TryTakeOrderedJob(followJob);
                }
            });
            yield return Toils_Jump.JumpIf(follow, () => !TargetA.Pawn.Downed);
            yield return Toils_General.Do(() => job.count = 1);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return follow;
            var go = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            go.AddPreTickAction(() => { if (TargetA.Pawn.Spawned && TargetA.Pawn.Position.DistanceToSquared(pawn.Position) > 100) EndJobWith(JobCondition.Incompletable); });
            yield return go;
            var wait = Toils_General.Wait(600, TargetIndex.B);
            wait.AddPreTickAction(() => { if (ColonyMissions.Extract(((Building_MissionClue)TargetB.Thing).Contract, TargetA.Pawn, pawn)) EndJobWith(JobCondition.Succeeded); });
            yield return wait;
        }
    }
}
