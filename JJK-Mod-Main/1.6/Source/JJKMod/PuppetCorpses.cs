using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    // These are constructed objects, not cursed spirits or resurrected people.
    public class GameComponent_Puppets : GameComponent
    {
        public List<Pawn> units = new List<Pawn>();
        public List<Pawn> provisionedOperators = new List<Pawn>();
        public GameComponent_Puppets(Game game) { }
        public static GameComponent_Puppets Instance => Current.Game?.GetComponent<GameComponent_Puppets>();
        public int Count(Pawn owner) => units.Count(p => p != null && !p.Dead && !p.Destroyed && p.TryGetComp<CompCursedCorpse>()?.controller == owner
            && p.TryGetComp<CompCommunicationPuppet>() == null && p.TryGetComp<CompThreeCores>()?.Mature != true);
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) units.RemoveAll(p => p == null || p.Destroyed || p.Dead);
            Scribe_Collections.Look(ref units, "cursedCorpses", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.Saving) provisionedOperators.RemoveAll(p => p == null || p.Destroyed);
            Scribe_Collections.Look(ref provisionedOperators, "provisionedOperators", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) provisionedOperators ??= new List<Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { units ??= new List<Pawn>(); units.RemoveAll(p => p == null || p.Destroyed || p.Dead); }
        }
    }

    public static class PuppetUtility
    {
        public static GeneDef Gene => DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_PuppetManipulation");
        public static bool Manipulator(Pawn p) => p?.genes?.HasActiveGene(Gene) == true;
        public static AbilityResource Energy(Pawn p) => p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
        // A conscious, physically disabled sorcerer can operate a remote puppet.
        public static bool Conscious(Pawn p) => p != null && !p.Dead && !p.Destroyed && p.Spawned && !p.InMentalState
            && p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) >= 0.3f;
        public static int Limit(Pawn p) => Manipulator(p) ? Mathf.Clamp(1 + (int)GameComponent_JJK.Instance.GetGrade(p) / 2, 1, 3) : 1;
        public static string BindReason(Pawn p, bool mech, Pawn unit = null)
        {
            if (!Conscious(p) || !JJK_GradeUtility.IsSorcerer(p)) return "JJK_PuppetNeedSorcerer".Translate();
            if (mech && !Manipulator(p)) return "JJK_PuppetNeedGene".Translate();
            if (unit?.TryGetComp<CompCommunicationPuppet>() != null)
            {
                if (!Manipulator(p)) return "JJK_PuppetNeedGene".Translate();
                return GameComponent_Puppets.Instance.units.Count(u => u != unit && u != null && !u.Dead && !u.Destroyed
                    && u.TryGetComp<CompCommunicationPuppet>() != null && u.TryGetComp<CompCursedCorpse>()?.controller == p) >= 3
                    ? "Maximum three communication puppets per creator." : null;
            }
            if (unit?.TryGetComp<CompCursedCorpse>()?.controller != p && GameComponent_Puppets.Instance.Count(p) >= Limit(p)) return "JJK_PuppetLimit".Translate(Limit(p));
            return null;
        }
        public static void Buff(Pawn p, string name, int ticks)
        {
            var def = DefDatabase<HediffDef>.GetNamed(name);
            var h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
            h.TryGetComp<HediffComp_Disappears>().ticksToDisappear = ticks;
        }
    }

    public class CompProperties_CursedCorpse : CompProperties
    {
        public bool mech;
        public CompProperties_CursedCorpse() { compClass = typeof(CompCursedCorpse); }
    }
    public enum PuppetMove { UltraCannon, UltraSpin, UltraShield, BoostOn, UltimateCannon }

    public class CompCursedCorpse : ThingComp
    {
        public Pawn controller;
        public float charge;
        public bool standby, follow = true, autoFire = true;
        public int nextShot, nextUltimate, nextSupport;
        public Pawn designatedTarget;
        public IntVec3 destination = IntVec3.Invalid;
        public Pawn Unit => (Pawn)parent;
        public bool Mech => ((CompProperties_CursedCorpse)props).mech;
        public bool Linked => Unit.TryGetComp<CompAbsolutePuppet>() is CompAbsolutePuppet absolute ? absolute.PilotReady : !Mech || (PuppetUtility.Conscious(controller) && controller.Map == Unit.Map
            && PuppetUtility.Manipulator(controller) && controller.Faction == Unit.Faction
            && !TechniqueSealUtil.IsSealed(controller) && !DomainAmplificationUtil.Active(controller)
            && !controller.health.hediffSet.HasHediff(JJK_DefOf.JJK_DomainBurnout));
        public bool Operational => Unit.Spawned && !Unit.Dead && !Unit.Downed && !standby && charge > 0f && Linked;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            var list = GameComponent_Puppets.Instance.units;
            if (!list.Contains(Unit)) list.Add(Unit);
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (controller?.Destroyed == true) controller = null;
                if (designatedTarget?.Destroyed == true) designatedTarget = null;
            }
            Scribe_References.Look(ref controller, "controller"); Scribe_References.Look(ref designatedTarget, "designatedTarget");
            Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref charge, "coreCharge"); Scribe_Values.Look(ref standby, "standby");
            Scribe_Values.Look(ref follow, "follow", true); Scribe_Values.Look(ref autoFire, "autoFire", true);
            Scribe_Values.Look(ref nextShot, "nextShot"); Scribe_Values.Look(ref nextUltimate, "nextUltimate"); Scribe_Values.Look(ref nextSupport, "nextSupport");
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!Unit.Spawned || Unit.Dead) return;
            if (Unit.IsHashIntervalTick(250) && Operational) charge = Mathf.Max(0, charge - 0.5f);
            if (Unit.IsHashIntervalTick(250) && Unit.Faction != null && Unit.Faction != Faction.OfPlayer && charge <= 60 && Linked) Recharge(controller);
            if (!Unit.IsHashIntervalTick(30)) return;
            if (!Operational) { if (Unit.CurJob?.def != JobDefOf.Wait) Unit.jobs.StopAll(); return; }
            if (Mech && autoFire && Unit.TryGetComp<CompAbsolutePuppet>() == null)
            {
                var enemy = Target(26);
                if (enemy != null) TryMove(PuppetMove.UltraCannon, enemy);
            }
        }
        public Pawn Target(float range)
        {
            bool Valid(Pawn p) => p != null && p.Spawned && p.Map == Unit.Map && !p.Dead && !p.Downed
                && p.HostileTo(Unit) && p.Position.DistanceToSquared(Unit.Position) <= range * range
                && GenSight.LineOfSight(Unit.Position, p.Position, Unit.Map);
            if (Valid(designatedTarget)) return designatedTarget;
            return Unit.Map.mapPawns.AllPawnsSpawned.Where(Valid).OrderBy(p => p.Position.DistanceToSquared(Unit.Position)).FirstOrDefault();
        }
        public string MoveReason(PuppetMove move, Pawn target)
        {
            if (!Mech || !Operational) return "JJK_PuppetOffline".Translate();
            if (Unit.TryGetComp<CompAbsolutePuppet>() != null) return "Use the cockpit's stored-energy weapons.";
            int now = Find.TickManager.TicksGame;
            if (now < (move == PuppetMove.UltimateCannon ? nextUltimate : move == PuppetMove.UltraShield || move == PuppetMove.BoostOn ? nextSupport : nextShot)) return "JJK_PuppetCooling".Translate();
            if (move == PuppetMove.UltimateCannon && GameComponent_JJK.Instance.GetGrade(controller) < SorcererGrade.Grade1) return "JJK_PuppetNeedGrade".Translate();
            if (charge < Cost(move)) return "JJK_PuppetLowCharge".Translate();
            bool attack = move == PuppetMove.UltraCannon || move == PuppetMove.UltraSpin || move == PuppetMove.UltimateCannon;
            float range = move == PuppetMove.UltraSpin ? 1.9f : move == PuppetMove.UltimateCannon ? 32 : 26;
            if (attack && (target == null || !target.Spawned || target.Map != Unit.Map || target.Dead || !target.HostileTo(Unit)
                || target.Position.DistanceToSquared(Unit.Position) > range * range || !GenSight.LineOfSight(Unit.Position, target.Position, Unit.Map))) return "JJK_PuppetTarget".Translate(range);
            return null;
        }
        public static float Cost(PuppetMove m) => m == PuppetMove.UltimateCannon ? 60 : m == PuppetMove.UltraShield ? 20 : m == PuppetMove.BoostOn ? 12 : m == PuppetMove.UltraSpin ? 15 : 6;
        public bool TryMove(PuppetMove move, Pawn target = null)
        {
            if (MoveReason(move, target) != null) return false;
            charge -= Cost(move);
            CombatPresentation.Play("Puppet", Unit.Map, Unit.Position);
            int now = Find.TickManager.TicksGame;
            if (move == PuppetMove.UltraShield || move == PuppetMove.BoostOn)
            {
                nextSupport = now + 900;
                PuppetUtility.Buff(Unit, move == PuppetMove.UltraShield ? "JJK_PuppetShield" : "JJK_PuppetBoost", 600);
            }
            else if (move == PuppetMove.UltraSpin)
            {
                nextShot = now + 180;
                target.TakeDamage(new DamageInfo(DamageDefOf.Stab, 40, 0.8f, -1, Unit));
            }
            else
            {
                bool ultimate = move == PuppetMove.UltimateCannon;
                if (ultimate) nextUltimate = now + 1800;
                nextShot = now + (ultimate ? 300 : 180);
                var projectile = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed(ultimate ? "JJK_Proj_UltimateCannon" : "JJK_Proj_UltraCannon"), Unit.Position, Unit.Map);
                projectile.Launch(Unit, Unit.DrawPos, target, target, ProjectileHitFlags.All);
            }
            return true;
        }
        public bool Recharge(Pawn user)
        {
            var ce = PuppetUtility.Energy(user);
            if (Unit.Dead || Unit.Destroyed || user == null || !user.Spawned || user.Map != Unit.Map || user.Faction != Unit.Faction
                || !PuppetUtility.Conscious(user) || ce == null || ce.energy < 30 || charge > 60) return false;
            ce.energy -= 30; charge = Mathf.Min(120, charge + 60); return true;
        }
        public bool Repair()
        {
            if (Unit.Dead) return false;
            var missing = Unit.health.hediffSet.GetMissingPartsCommonAncestors().FirstOrDefault(h => !CompThreeCores.IsSoulCore(h.Part));
            if (missing != null) { Unit.health.RestorePart(missing.Part); return true; }
            float pool = 30;
            foreach (var injury in Unit.health.hediffSet.hediffs.OfType<Hediff_Injury>().OrderByDescending(h => h.Severity).ToList())
            { float n = Mathf.Min(pool, injury.Severity); injury.Heal(n); pool -= n; if (pool <= 0) break; }
            return pool < 30;
        }
        public bool Damaged => Unit.health.hediffSet.hediffs.Any(h => h is Hediff_Injury || h is Hediff_MissingPart);
        public override string CompInspectStringExtra() => "JJK_PuppetStatus".Translate(controller?.LabelShortCap ?? "JJK_PuppetUnbound".Translate().ToString(), charge.ToString("F1"), Operational ? "JJK_PuppetOnline".Translate() : "JJK_PuppetOffline".Translate());
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!Unit.Spawned || Unit.Dead || selPawn.Faction != Unit.Faction || selPawn.Faction != Faction.OfPlayer) yield break;
            if ((controller == null || controller.Dead || controller.Destroyed) && Unit.TryGetComp<CompThreeCores>()?.Mature != true)
            {
                string reason = PuppetUtility.BindReason(selPawn, Mech, Unit);
                yield return new FloatMenuOption(reason ?? "JJK_PuppetBind".Translate().ToString(), reason != null ? (Action)null : () => { controller = selPawn; });
            }
            if (charge <= 60 && PuppetUtility.Energy(selPawn)?.energy >= 30 && selPawn.CanReserveAndReach(Unit, PathEndMode.Touch, Danger.Some))
                yield return new FloatMenuOption("JJK_PuppetRecharge".Translate(), () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_RechargePuppet"), Unit)));
            Thing kit = Unit.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_PuppetRepairKit"))
                .FirstOrDefault(t => !t.IsForbidden(selPawn) && selPawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some));
            if (Damaged && kit != null && selPawn.CanReserveAndReach(Unit, PathEndMode.Touch, Danger.Some))
                yield return new FloatMenuOption("JJK_PuppetRepair".Translate(), () => { var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_RepairPuppet"), Unit, kit); job.count = 1; selPawn.jobs.TryTakeOrderedJob(job); });
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Unit.Faction != Faction.OfPlayer || Unit.Dead) yield break;
            var moveOrder = new Command_Target { defaultLabel = "JJK_PuppetMove".Translate(), defaultDesc = "JJK_PuppetMoveDesc".Translate(), icon = TexCommand.Draft,
                targetingParams = TargetingParameters.ForCell(), action = t => {
                    if (Operational && t.Cell.Standable(Unit.Map) && Unit.CanReach(t.Cell, PathEndMode.OnCell, Danger.Deadly))
                    { destination = t.Cell; follow = false; Unit.jobs.StopAll(); }
                    else Messages.Message("JJK_PuppetMoveFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                } };
            if (!Operational) moveOrder.Disable("JJK_PuppetOffline".Translate());
            yield return moveOrder;
            yield return new Command_Toggle { defaultLabel = "JJK_PuppetStandby".Translate(), defaultDesc = "JJK_PuppetStandbyDesc".Translate(), isActive = () => standby, toggleAction = () => { standby = !standby; Unit.jobs.StopAll(); }, icon = TexCommand.Draft };
            if (Unit.TryGetComp<CompIndependentCompanion>()?.Independent != true)
                yield return new Command_Toggle { defaultLabel = "JJK_PuppetFollow".Translate(), defaultDesc = "JJK_PuppetFollowDesc".Translate(), isActive = () => follow, toggleAction = () => { follow = !follow; destination = IntVec3.Invalid; Unit.jobs.StopAll(); }, icon = TexCommand.Draft };
            if (controller?.Spawned == true && controller.Map == Unit.Map)
                yield return new Command_Action { defaultLabel = "JJK_PuppetFindController".Translate(), icon = TexCommand.Draft, action = () => CameraJumper.TryJumpAndSelect(controller) };
            if (!Mech || Unit.TryGetComp<CompAbsolutePuppet>() != null) yield break;
            yield return new Command_Toggle { defaultLabel = "JJK_PuppetAutoFire".Translate(), defaultDesc = "JJK_PuppetAutoFireDesc".Translate(), isActive = () => autoFire, toggleAction = () => autoFire = !autoFire, icon = TexCommand.Attack };
            foreach (PuppetMove m in Enum.GetValues(typeof(PuppetMove)))
            {
                PuppetMove move = m;
                bool targeted = move == PuppetMove.UltraCannon || move == PuppetMove.UltraSpin || move == PuppetMove.UltimateCannon;
                Command cmd;
                if (targeted) cmd = new Command_Target { targetingParams = new TargetingParameters { canTargetPawns = true, canTargetBuildings = false, canTargetLocations = false }, action = t => { if (!TryMove(move, t.Pawn)) Messages.Message(MoveReason(move, t.Pawn) ?? "JJK_PuppetOffline".Translate(), MessageTypeDefOf.RejectInput, false); } };
                else cmd = new Command_Action { action = () => TryMove(move) };
                cmd.defaultLabel = ("JJK_Puppet_" + move).Translate(); cmd.defaultDesc = ("JJK_Puppet_" + move + "Desc").Translate(); cmd.icon = TexCommand.Attack;
                string blocked = MoveReason(move, null);
                if (!Operational || charge < Cost(move) || (!targeted && blocked != null)
                    || (move == PuppetMove.UltimateCannon && GameComponent_JJK.Instance.GetGrade(controller) < SorcererGrade.Grade1)) cmd.Disable(blocked ?? "JJK_PuppetLowCharge".Translate());
                yield return cmd;
            }
        }
    }

    public class JobGiver_CursedCorpse : ThinkNode_JobGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            var comp = pawn.TryGetComp<CompCursedCorpse>();
            Job Wait() { var j = JobMaker.MakeJob(JobDefOf.Wait, 60); j.checkOverrideOnExpire = true; return j; }
            if (comp?.Operational != true) return Wait();
            if (comp.destination.IsValid && comp.destination != pawn.Position)
            {
                if (comp.destination.InBounds(pawn.Map) && comp.destination.Standable(pawn.Map) && pawn.CanReach(comp.destination, PathEndMode.OnCell, Danger.Deadly))
                    return JobMaker.MakeJob(JobDefOf.Goto, comp.destination);
                comp.destination = IntVec3.Invalid;
            }
            Pawn target = pawn.TryGetComp<CompCommunicationPuppet>() != null ? null : comp.Target(comp.Mech ? 26 : 15);
            if (target != null && (!comp.Mech || target.Position.DistanceToSquared(pawn.Position) < 4))
            {
                if (pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) { var job = JobMaker.MakeJob(JobDefOf.AttackMelee, target); job.expiryInterval = 120; job.checkOverrideOnExpire = true; return job; }
            }
            var companion = pawn.TryGetComp<CompIndependentCompanion>();
            if (target == null && companion?.Independent == true)
                return companion.Routine() ?? Wait();
            if (target == null && comp.follow && comp.controller?.Spawned == true && comp.controller.Map == pawn.Map
                && comp.controller.Faction == pawn.Faction && pawn.Position.DistanceToSquared(comp.controller.Position) > 25)
            { var cell = CellFinder.RandomClosewalkCellNear(comp.controller.Position, pawn.Map, 3); if (pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) return JobMaker.MakeJob(JobDefOf.Goto, cell); }
            return Wait();
        }
    }

    public class CompProperties_ActivatePuppet : CompProperties_UseEffect
    {
        public PawnKindDef kind;
        public bool mech;
        public bool communication;
        public CompProperties_ActivatePuppet() { compClass = typeof(CompUseEffect_ActivatePuppet); }
    }
    public class CompUseEffect_ActivatePuppet : CompUseEffect
    {
        public new CompProperties_ActivatePuppet Props => (CompProperties_ActivatePuppet)props;
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            string reason = Props.communication ? (PuppetUtility.Conscious(p) && PuppetUtility.Manipulator(p) ? null : "JJK_PuppetNeedGene".Translate().ToString()) : PuppetUtility.BindReason(p, Props.mech);
            if (reason != null) return reason;
            if (Props.communication && GameComponent_Puppets.Instance.units.Count(u => u != null && !u.Dead && !u.Destroyed && u.TryGetComp<CompCommunicationPuppet>() != null && u.TryGetComp<CompCursedCorpse>()?.controller == p) >= 3) return "Maximum three communication puppets per creator.";
            if (PuppetUtility.Energy(p)?.energy < 30 || PuppetUtility.Energy(p) == null) return "JJK_PuppetNeedEnergy".Translate();
            return base.CanBeUsedBy(p);
        }
        public override void DoEffect(Pawn usedBy)
        {
            // Consume the shell only after all conditions still hold and a unit exists.
            if (!CanBeUsedBy(usedBy).Accepted || !parent.Spawned || parent.Map != usedBy.Map) return;
            Pawn unit = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Props.kind, usedBy.Faction,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 0,
                fixedChronologicalAge: 0, fixedGender: Gender.None));
            var comp = unit.TryGetComp<CompCursedCorpse>(); comp.controller = usedBy; comp.charge = 60;
            if (Props.communication) comp.follow = false;
            GenSpawn.Spawn(unit, CellFinder.RandomClosewalkCellNear(parent.Position, parent.Map, 2), parent.Map);
            PuppetUtility.Energy(usedBy).energy -= 30;
            parent.SplitOff(1).Destroy();
            Messages.Message("JJK_PuppetActivated".Translate(unit.LabelCap, usedBy.LabelShortCap), unit, MessageTypeDefOf.PositiveEvent);
        }
    }

    public class JobDriver_RechargePuppet : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => job.targetA.Pawn?.TryGetComp<CompCursedCorpse>()?.Recharge(pawn));
        }
    }
    public class JobDriver_RepairPuppet : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetB, job, 1, 1, null, errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(300).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => {
                var comp = job.targetA.Pawn?.TryGetComp<CompCursedCorpse>();
                if (comp != null && comp.Unit.Faction == pawn.Faction && pawn.carryTracker.CarriedThing?.def.defName == "JJK_PuppetRepairKit" && comp.Repair()) pawn.carryTracker.CarriedThing.SplitOff(1).Destroy();
            });
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class Patch_PuppetNeeds
    {
        public static bool Prefix(Pawn ___pawn, ref bool __result) { if (___pawn.TryGetComp<CompCursedCorpse>() == null) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(HediffUtility), "CanHealNaturally")]
    public static class Patch_PuppetNaturalHealing
    {
        public static void Postfix(Hediff_Injury __0, ref bool __result) { if (__0.pawn?.TryGetComp<CompCursedCorpse>() != null) __result = false; }
    }
    [HarmonyPatch(typeof(HediffUtility), "CanHealFromTending")]
    public static class Patch_PuppetTendedHealing
    {
        public static void Postfix(Hediff_Injury __0, ref bool __result) { if (__0.pawn?.TryGetComp<CompCursedCorpse>() != null) __result = false; }
    }
}
