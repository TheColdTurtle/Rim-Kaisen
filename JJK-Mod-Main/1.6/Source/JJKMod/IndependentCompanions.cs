using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace JJKMod
{
    public enum CompanionRole { Companion, Hauler, Rescuer, Guard }
    public enum CompanionTemperament { Cheerful, Steadfast, Gentle }
    public class CompProperties_IndependentCompanion : CompProperties
    { public CompProperties_IndependentCompanion() { compClass = typeof(CompIndependentCompanion); } }

    // Social state belongs to the construct, independently of its creator link.
    public class CompIndependentCompanion : ThingComp
    {
        public Pawn companion;
        public string rememberedName;
        public float bond = 20;
        public int nextSocial, griefUntil, knownLostCores, nextWorkScan;
        public bool initialized, lossRemembered, deathNotified;
        public CompanionRole role;
        public CompanionTemperament temperament;
        public Pawn Unit => (Pawn)parent;
        public CompCursedCorpse Core => Unit.TryGetComp<CompCursedCorpse>();
        public CompThreeCores Cores => Unit.TryGetComp<CompThreeCores>();
        public bool Independent => Cores?.Mature == true;
        public bool Grieving => Find.TickManager.TicksGame < griefUntil;
        private int GriefDuration => temperament == CompanionTemperament.Steadfast ? 120000 : 180000;
        public bool CanAct => Independent && Core.Operational && Unit.Faction == Faction.OfPlayer;
        public static bool Friend(Pawn p, Pawn unit) => p != null && !p.Dead && !p.Destroyed && p.IsColonist && p.Faction == unit.Faction && p.Faction == Faction.OfPlayer;
        public bool CanVisit(Pawn p) => CanAct && Friend(p, Unit) && p.Spawned && p.Map == Unit.Map && !p.Downed && !p.InMentalState && p.Awake() && !p.Drafted;
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); Initialize(); }
        public void Initialize()
        {
            if (initialized) return;
            initialized = true; temperament = (CompanionTemperament)(Unit.thingIDNumber % 3);
            if (Friend(Core.controller, Unit)) companion = Core.controller;
            rememberedName = companion?.LabelShortCap; knownLostCores = Cores.lostCores;
        }
        public void UpdateEmotions(int now)
        {
            Initialize(); if (!Independent) return;
            if (companion != null && (companion.Dead || companion.Destroyed) && !lossRemembered)
            {
                lossRemembered = true; griefUntil = Math.Max(griefUntil, now + GriefDuration); bond = Mathf.Max(0, bond - 10);
                if (Unit.Faction == Faction.OfPlayer) Messages.Message("JJK_CompanionMourning".Translate(Unit.LabelShortCap, rememberedName ?? companion.LabelShortCap), Unit, MessageTypeDefOf.NegativeEvent, false);
            }
            int lost = Cores.lostCores;
            if ((lost & ~knownLostCores) != 0) griefUntil = Math.Max(griefUntil, now + GriefDuration);
            knownLostCores |= lost;
        }
        public override void CompTick() { if (Unit.Spawned && !Unit.Dead && Unit.IsHashIntervalTick(250)) UpdateEmotions(Find.TickManager.TicksGame); }
        public bool Befriend(Pawn p)
        {
            if (!CanAct || !Friend(p, Unit) || p == companion || !p.Spawned || p.Map != Unit.Map) return false;
            companion = p; rememberedName = p.LabelShortCap; bond = 20; lossRemembered = false;
            return true; // New company does not erase grief or reset the interaction cooldown.
        }
        public bool Visit(Pawn p)
        {
            int now = Find.TickManager.TicksGame;
            if (!CanVisit(p) || now < nextSocial || !Unit.Position.InHorDistOf(p.Position, 3)) return false;
            nextSocial = now + 30000;
            if (p == companion) bond = Mathf.Min(100, bond + (temperament == CompanionTemperament.Gentle ? 8 : 6));
            var memories = p.needs?.mood?.thoughts?.memories;
            memories?.RemoveMemoriesOfDef(DefDatabase<ThoughtDef>.GetNamed("JJK_CompanionCompany"));
            memories?.RemoveMemoriesOfDef(DefDatabase<ThoughtDef>.GetNamed("JJK_CompanionTrustedCompany"));
            memories?.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed(p == companion && bond >= 60 ? "JJK_CompanionTrustedCompany" : "JJK_CompanionCompany"));
            p.needs?.joy?.GainJoy(temperament == CompanionTemperament.Cheerful ? 0.10f : 0.06f, JoyKindDefOf.Social);
            if (Grieving) griefUntil = Math.Max(now, griefUntil - 5000);
            return true;
        }
        public void SetRole(CompanionRole value)
        {
            if (!Enum.IsDefined(typeof(CompanionRole), value)) return;
            role = value; Core.destination = IntVec3.Invalid; Core.follow = false; nextWorkScan = 0; Unit.jobs?.StopAll();
        }
        public bool CanRepair => Unit.health.hediffSet.hediffs.Any(h => h is Hediff_Injury || (h is Hediff_MissingPart && !CompThreeCores.IsSoulCore(h.Part)));
        private Job SocialJob()
        {
            if (Find.TickManager.TicksGame < nextSocial) return null;
            Pawn friend = CanVisit(companion) ? companion : Unit.Map.mapPawns.FreeColonistsSpawned.Where(CanVisit).OrderBy(p => p.Position.DistanceToSquared(Unit.Position)).FirstOrDefault();
            return friend != null && friend.Position.InAllowedArea(Unit) && Unit.CanReserveAndReach(friend, PathEndMode.Touch, Danger.Some)
                ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_CompanionVisit"), friend) : null;
        }
        public void NotifyDeath()
        {
            if (!Unit.Dead || !Independent || deathNotified) return;
            deathNotified = true;
            if (Friend(companion, Unit)) companion.needs?.mood?.thoughts?.memories?.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_CompanionLost"));
        }
        public Job Routine()
        {
            if (!CanAct || Core.destination.IsValid || Core.Target(20) != null) return null;
            int now = Find.TickManager.TicksGame;
            if (now < nextWorkScan) return null;
            nextWorkScan = now + 250;
            if (CanRepair)
            {
                var kit = Unit.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_PuppetRepairKit"))
                    .Where(t => !t.IsForbidden(Unit) && t.Position.InAllowedArea(Unit) && Unit.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some))
                    .OrderBy(t => t.Position.DistanceToSquared(Unit.Position)).FirstOrDefault();
                if (kit != null) { var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_CompanionSelfRepair"), kit); job.count = 1; return job; }
            }
            if (Grieving && role != CompanionRole.Guard) { var comfort = SocialJob(); if (comfort != null) return comfort; }
            if (role == CompanionRole.Rescuer)
            {
                var work = new WorkGiver_RescueDowned();
                foreach (var p in Unit.Map.mapPawns.FreeColonistsSpawned.Where(p => p.Downed && p.Position.InAllowedArea(Unit)).OrderBy(p => p.Position.DistanceToSquared(Unit.Position)))
                    if (work.HasJobOnThing(Unit, p)) return work.JobOnThing(Unit, p);
            }
            if (role == CompanionRole.Hauler)
            {
                foreach (var t in Unit.Map.listerHaulables.ThingsPotentiallyNeedingHauling()
                    .Where(t => !(t is Corpse) && !t.IsForbidden(Unit) && t.Position.InAllowedArea(Unit)).OrderBy(t => t.Position.DistanceToSquared(Unit.Position)))
                {
                    if (!Unit.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    var job = HaulAIUtility.HaulToStorageJob(Unit, t, false); if (job != null) return job;
                }
            }
            if (role != CompanionRole.Guard && now >= nextSocial)
            {
                var social = SocialJob(); if (social != null) return social;
            }
            if (role == CompanionRole.Companion && CanVisit(companion) && Unit.Position.DistanceToSquared(companion.Position) > 36)
            {
                var cell = CellFinder.RandomClosewalkCellNear(companion.Position, Unit.Map, 3);
                if (cell.InAllowedArea(Unit) && Unit.CanReach(cell, PathEndMode.OnCell, Danger.Some)) return JobMaker.MakeJob(JobDefOf.Goto, cell);
            }
            return null;
        }
        public override void PostExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && companion?.Destroyed == true) { UpdateEmotions(Find.TickManager.TicksGame); companion = null; }
            Scribe_References.Look(ref companion, "companionFriend"); Scribe_Values.Look(ref rememberedName, "companionFriendName");
            Scribe_Values.Look(ref bond, "companionBond", 20); Scribe_Values.Look(ref nextSocial, "companionNextSocial");
            Scribe_Values.Look(ref griefUntil, "companionGriefUntil"); Scribe_Values.Look(ref knownLostCores, "companionKnownLostCores");
            Scribe_Values.Look(ref initialized, "companionInitialized"); Scribe_Values.Look(ref lossRemembered, "companionLossRemembered");
            Scribe_Values.Look(ref deathNotified, "companionDeathNotified");
            Scribe_Values.Look(ref role, "companionRole"); Scribe_Values.Look(ref temperament, "companionTemperament");
        }
        public override string CompInspectStringExtra() => !Independent ? "JJK_CompanionImmature".Translate() : "JJK_CompanionStatus".Translate(
            ("JJK_CompanionRole" + role).Translate(), ("JJK_CompanionTemperament" + temperament).Translate(), rememberedName ?? "JJK_CompanionNoFriend".Translate().ToString(),
            Mathf.RoundToInt(bond), Grieving ? "JJK_CompanionGrieving".Translate() : "JJK_CompanionContent".Translate());
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Unit.Faction != Faction.OfPlayer || Unit.Dead) yield break;
            var cmd = new Command_Action { defaultLabel = "JJK_CompanionOpen".Translate(), defaultDesc = "JJK_CompanionDescription".Translate(), icon = TexCommand.ClearPrioritizedWork,
                action = () => Find.WindowStack.Add(new Window_IndependentCompanion(this)) };
            if (!Independent) cmd.Disable("JJK_CompanionImmature".Translate()); yield return cmd;
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CompanionDeath
    { public static void Postfix(Pawn __instance) => __instance.TryGetComp<CompIndependentCompanion>()?.NotifyDeath(); }
    public class JobDriver_CompanionVisit : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => pawn.TryGetComp<CompIndependentCompanion>()?.CanVisit(job.targetA.Pawn) != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(300).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => pawn.TryGetComp<CompIndependentCompanion>().Visit(job.targetA.Pawn));
        }
    }
    public class JobDriver_CompanionSelfRepair : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => pawn.TryGetComp<CompIndependentCompanion>()?.CanAct != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_General.Wait(300).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => {
                var carried = pawn.carryTracker.CarriedThing;
                if (carried?.def.defName == "JJK_PuppetRepairKit" && pawn.TryGetComp<CompCursedCorpse>().Repair()) carried.SplitOff(1).Destroy();
            });
        }
    }
    public class Window_IndependentCompanion : Window
    {
        private readonly CompIndependentCompanion comp;
        private string name;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(650, 650);
        public Window_IndependentCompanion(CompIndependentCompanion comp)
        { this.comp = comp; name = comp.Unit.Name?.ToStringFull ?? comp.Unit.LabelShort; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            if (comp.Unit.Destroyed || comp.Unit.Dead) { Close(); return; }
            var view = new Rect(0, 0, rect.width - 20, 720 + (comp.Unit.Map?.mapPawns.FreeColonistsSpawned.Count ?? 0) * 34);
            Widgets.BeginScrollView(rect, ref scroll, view); var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_CompanionDescription".Translate()); list.Label(comp.CompInspectStringExtra());
            name = Widgets.TextField(list.GetRect(30), name, 40);
            if (Widgets.ButtonText(list.GetRect(30), "JJK_CompanionRename".Translate()) && !string.IsNullOrWhiteSpace(name)) comp.Unit.Name = new NameSingle(name.Trim());
            foreach (CompanionRole role in Enum.GetValues(typeof(CompanionRole)))
                if (Widgets.ButtonText(list.GetRect(30), ("JJK_CompanionRole" + role).Translate())) comp.SetRole(role);
            list.Label("JJK_CompanionChooseFriend".Translate());
            if (comp.Unit.Map != null) foreach (var p in comp.Unit.Map.mapPawns.FreeColonistsSpawned)
                if (Widgets.ButtonText(list.GetRect(30), p.LabelShortCap)) comp.Befriend(p);
            list.End(); Widgets.EndScrollView();
        }
    }
}
