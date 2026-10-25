using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
    public static class Patch_AbsoluteCockpitHolder
    {
        public static void Postfix(Pawn __instance, List<IThingHolder> outChildren)
        {
            var cockpit = __instance.TryGetComp<CompAbsolutePuppet>();
            if (cockpit != null && !outChildren.Contains(cockpit)) outChildren.Add(cockpit);
        }
    }
    public class CompProperties_AbsolutePuppet : CompProperties
    {
        public CompProperties_AbsolutePuppet() { compClass = typeof(CompAbsolutePuppet); }
    }
    public enum AbsoluteMove { OneYear, Miracle, Pigeon, CockpitDomain, DomainInjection }
    public class CompAbsolutePuppet : ThingComp, IThingHolder
    {
        private ThingOwner<Pawn> cockpit;
        public float reservoir;
        public int cartridges, nextAttack, shieldUntil;
        public CompAbsolutePuppet() { cockpit = new ThingOwner<Pawn>(this); }
        public Pawn Unit => (Pawn)parent;
        public CompCursedCorpse Core => Unit.TryGetComp<CompCursedCorpse>();
        public Pawn Pilot => cockpit.InnerListForReading.FirstOrDefault();
        public bool PilotReady => Pilot != null && !Pilot.Dead && !Pilot.InMentalState && Pilot.Faction == Unit.Faction
            && Pilot.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) >= 0.3f && PuppetUtility.Manipulator(Pilot)
            && !TechniqueSealUtil.IsSealed(Pilot) && !DomainAmplificationUtil.Active(Pilot) && !Pilot.health.hediffSet.HasHediff(JJK_DefOf.JJK_DomainBurnout);
        public bool Shielded => PilotReady && Find.TickManager.TicksGame < shieldUntil;
        public new IThingHolder ParentHolder => Unit;
        public ThingOwner GetDirectlyHeldThings() => cockpit;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, cockpit);
        public string BoardReason(Pawn p)
        {
            if (Pilot != null) return "Cockpit occupied.";
            if (p?.Spawned != true || !Unit.Spawned || Unit.Dead || Unit.Downed || p.Map != Unit.Map || p.Faction != Unit.Faction || !PuppetUtility.Conscious(p) || !PuppetUtility.Manipulator(p)) return "Requires a conscious allied Puppet Manipulation sorcerer.";
            if (p != Core.controller) return "Only the bound controller can pilot this frame.";
            if (GameComponent_JJK.Instance.GetGrade(p) < SorcererGrade.Grade1) return "Requires Grade 1.";
            return null;
        }
        public bool Board(Pawn p)
        {
            if (BoardReason(p) != null || p.Position.DistanceToSquared(Unit.Position) > 9) return false;
            IntVec3 cell = p.Position; p.jobs.StopAll(); p.DeSpawn();
            if (!cockpit.TryAdd(p)) { GenSpawn.Spawn(p, cell, Unit.Map); return false; }
            Core.standby = false; Core.follow = false; return true;
        }
        public bool Eject(Map map = null)
        {
            map ??= Unit.MapHeld; var pilot = Pilot;
            if (pilot == null || map == null) return false;
            IntVec3 cell = Unit.PositionHeld.InBounds(map) ? Unit.PositionHeld : map.Center;
            if (!cockpit.TryDrop(pilot, cell, map, ThingPlaceMode.Near, out Pawn dropped)) return false;
            Core.standby = true; Core.destination = IntVec3.Invalid; shieldUntil = 0; return true;
        }
        public bool Deposit(Pawn p)
        {
            var ce = PuppetUtility.Energy(p);
            if (Unit.Dead || p == null || p.Faction != Unit.Faction || (p != Pilot && (!p.Spawned || p.Map != Unit.Map)) || ce == null || ce.energy < 50 || reservoir > 2350) return false;
            ce.energy -= 50; reservoir += 50; return true;
        }
        public bool LoadCartridge() { if (cartridges >= 4 || Unit.Dead) return false; cartridges++; return true; }
        public float Cost(AbsoluteMove move) => move == AbsoluteMove.OneYear ? 120 : move == AbsoluteMove.Miracle ? 240 : move == AbsoluteMove.Pigeon ? 600 : 0;
        public string FireReason(AbsoluteMove move, Pawn target)
        {
            if (!Core.Operational || !PilotReady) return "Pilot unavailable or frame inactive.";
            if (Find.TickManager.TicksGame < nextAttack) return "Weapons recovering.";
            if (reservoir < Cost(move)) return "Insufficient stored energy.";
            if ((move == AbsoluteMove.CockpitDomain || move == AbsoluteMove.DomainInjection) && cartridges < 1) return "Load a Simple Domain cartridge first.";
            if (move != AbsoluteMove.CockpitDomain)
            {
                float range = move == AbsoluteMove.DomainInjection ? 1.9f : 40;
                if (target?.Spawned != true || target.Dead || target.Map != Unit.Map || !target.HostileTo(Unit) || target.Position.DistanceToSquared(Unit.Position) > range * range || !GenSight.LineOfSight(Unit.Position, target.Position, Unit.Map)) return "Target must be a visible enemy within " + range + " tiles of the frame.";
            }
            return null;
        }
        public bool Fire(AbsoluteMove move, Pawn target = null)
        {
            if (FireReason(move, target) != null) return false;
            reservoir -= Cost(move); nextAttack = Find.TickManager.TicksGame + 300;
            CombatPresentation.Play("Puppet", Unit.Map, Unit.Position);
            if (move == AbsoluteMove.CockpitDomain) { cartridges--; shieldUntil = Find.TickManager.TicksGame + 600; return true; }
            if (move == AbsoluteMove.DomainInjection)
            {
                cartridges--; var damage = new DamageInfo(DamageDefOf.Stab, 65, 0, -1, Unit); damage.SetIgnoreArmor(true); target.TakeDamage(damage); return true;
            }
            string projectile = move == AbsoluteMove.OneYear ? "JJK_Proj_AbsoluteOneYear" : move == AbsoluteMove.Miracle ? "JJK_Proj_MiracleCannon" : "JJK_Proj_PigeonViola";
            for (int i = 0; i < (move == AbsoluteMove.Pigeon ? 5 : 1); i++)
            {
                var p = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed(projectile), Unit.Position, Unit.Map);
                p.Launch(Unit, Unit.DrawPos, target, target, ProjectileHitFlags.All);
            }
            return true;
        }
        public override void CompTick()
        {
            if (!Unit.Spawned || Unit.Dead) return;
            // A cockpit is not cryptosleep: the pilot's health and needs keep ticking.
            cockpit.DoTick();
            if (Pilot != null && (Pilot.Dead || Unit.Downed)) Eject();
            if (Unit.IsHashIntervalTick(250) && PilotReady && Core.charge < 1 && reservoir >= 10) { reservoir -= 10; Core.charge += 10; }
        }
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null) { Eject(prevMap); }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) { Eject(map); }
        public override void PostDestroy(DestroyMode mode, Map previousMap) { Eject(previousMap); }
        public override void PostExposeData()
        {
            Scribe_Deep.Look(ref cockpit, "cockpit", this); cockpit ??= new ThingOwner<Pawn>(this);
            Scribe_Values.Look(ref reservoir, "storedEnergy"); Scribe_Values.Look(ref cartridges, "cartridges");
            Scribe_Values.Look(ref nextAttack, "nextAttack"); Scribe_Values.Look(ref shieldUntil, "shieldUntil");
        }
        public override string CompInspectStringExtra() => "Cockpit: " + (Pilot?.LabelShortCap ?? "empty") + "\nStored energy: " + reservoir.ToString("F0") + " / 2400\nSimple Domain cartridges: " + cartridges + " / 4";
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn p)
        {
            if (Unit.Faction != p.Faction || Unit.Dead || !p.CanReserveAndReach(Unit, PathEndMode.Touch, Danger.Some)) yield break;
            string board = BoardReason(p);
            yield return new FloatMenuOption(board ?? "Enter Mode: Absolute", board == null ? (Action)(() => p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AbsoluteBoard"), Unit))) : null);
            if (PuppetUtility.Energy(p)?.energy >= 50 && reservoir <= 2350)
                yield return new FloatMenuOption("Store 50 cursed energy", () => p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AbsoluteDeposit"), Unit)));
            var tube = Unit.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_DomainCartridge")).FirstOrDefault(t => !t.IsForbidden(p) && p.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some));
            if (tube != null && cartridges < 4)
                yield return new FloatMenuOption("Load Simple Domain cartridge", () => { var j = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AbsoluteLoad"), Unit, tube); j.count = 1; p.jobs.TryTakeOrderedJob(j); });
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Unit.Faction != Faction.OfPlayer || Unit.Dead) yield break;
            if (Pilot != null)
            {
                yield return new Command_Action { defaultLabel = "Exit cockpit", defaultDesc = "Eject the same pilot with their existing injuries, genes and equipment.", icon = TexCommand.Draft, action = () => Eject() };
                yield return new Command_Action { defaultLabel = "Store 50 CE", defaultDesc = "Transfer 50 of the pilot's CE to the reservoir. Stored energy is spent by the giant frame's weapons.", icon = TexCommand.Draft, action = () => Deposit(Pilot) };
            }
            foreach (AbsoluteMove value in Enum.GetValues(typeof(AbsoluteMove)))
            {
                AbsoluteMove move = value; Command command;
                if (move == AbsoluteMove.CockpitDomain) command = new Command_Action { action = () => Fire(move) };
                else command = new Command_Target { targetingParams = new TargetingParameters { canTargetPawns = true, canTargetBuildings = false, canTargetLocations = false }, action = t => { if (!Fire(move, t.Pawn)) Messages.Message(FireReason(move, t.Pawn), MessageTypeDefOf.RejectInput, false); } };
                command.defaultLabel = move == AbsoluteMove.OneYear ? "One-year charge" : move == AbsoluteMove.Miracle ? "Miracle Cannon" : move == AbsoluteMove.Pigeon ? "Pigeon Viola" : move == AbsoluteMove.CockpitDomain ? "Cockpit Simple Domain" : "Domain injection";
                command.defaultDesc = move == AbsoluteMove.CockpitDomain ? "Spend one cartridge for 600 ticks of cockpit/frame sure-hit protection; ordinary damage still applies."
                    : move == AbsoluteMove.DomainInjection ? "Spend one cartridge for an armor-ignoring 65-damage stab at range 1.9."
                    : "Spend " + Cost(move) + " stored energy. Range 40. One-year and Miracle blasts can harm allies. Pigeon Viola launches five tracking bolts. The year names are canon; these reservoir costs are a gameplay adaptation.";
                command.icon = TexCommand.Attack;
                if (!Core.Operational || reservoir < Cost(move) || Find.TickManager.TicksGame < nextAttack || (Cost(move) == 0 && cartridges == 0)) command.Disable("Check pilot, standby, stored energy, cartridges and weapon recovery.");
                yield return command;
            }
        }
    }
    public class JobDriver_AbsoluteService : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
            && (!job.targetB.IsValid || pawn.Reserve(job.targetB, job, 1, 1, null, errorOnFailed));
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            if (job.targetB.IsValid) { yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch); yield return Toils_Haul.StartCarryThing(TargetIndex.B); }
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => {
                var c = job.targetA.Pawn?.TryGetComp<CompAbsolutePuppet>(); if (c == null || c.Unit.Faction != pawn.Faction) return;
                if (job.def.defName == "JJK_AbsoluteBoard") c.Board(pawn);
                else if (job.def.defName == "JJK_AbsoluteDeposit") c.Deposit(pawn);
                else if (pawn.carryTracker.CarriedThing?.def.defName == "JJK_DomainCartridge" && c.LoadCartridge()) pawn.carryTracker.CarriedThing.SplitOff(1).Destroy();
            });
        }
    }

    // Retarget from the projectile's actual current position, retaining vanilla collision
    // and interception. Recalculating flight time prevents a moving target becoming an instant hit.
    public class Projectile_PigeonViola : Bullet
    {
        public override void TickInterval(int delta)
        {
            if (intendedTarget.Thing is Pawn target && target.Spawned && target.Map == Map && !target.Dead)
            {
                Vector3 here = ExactPosition;
                origin = here; destination = target.DrawPos;
                ticksToImpact = Mathf.Max(1, Mathf.CeilToInt((destination - origin).magnitude / (def.projectile.speed / 60f)));
            }
            base.TickInterval(delta);
        }
    }
}
