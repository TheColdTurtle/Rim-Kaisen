using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class VesselDevelopment
    {
        public static void Seized(Pawn p)
        {
            if (p.Faction != Faction.OfPlayer) return;
            SorceryDevelopment.State(p).rescueDeadline = SorceryDevelopment.Now + 6000;
            Find.LetterStack.ReceiveLetter("JJK_DeepRescue".Translate(), "JJK_DeepRescueText".Translate(p.LabelShortCap), LetterDefOf.ThreatSmall, p);
        }
        public static void Tick(SorceryRecord r)
        {
            if (r.rescueDeadline == 0 || SorceryDevelopment.Now < r.rescueDeadline) return;
            r.rescueDeadline = 0;
            var h = r.pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_SukunaInfluence);
            if (h != null) h.Severity = System.Math.Min(1, h.Severity + .1f);
            r.pawn.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_VesselAbandoned"));
            Messages.Message("JJK_DeepRescueFailed".Translate(r.pawn.LabelShortCap), r.pawn, MessageTypeDefOf.NegativeEvent);
        }
        public static bool CanConfront(Pawn p) => SorceryDevelopment.CanAct(p) && HediffComp_SukunaVessel.For(p) is HediffComp_SukunaVessel v
            && !v.Possessed && v.loanUntil == 0 && !v.debt && SorceryDevelopment.State(p).rescueDeadline == 0
            && SorceryDevelopment.Now >= SorceryDevelopment.State(p).nextConfrontation;
        public static void Confront(Pawn p)
        {
            if (CanConfront(p)) p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_InnerConfrontation")));
        }
        public static bool FinishConfrontation(Pawn p)
        {
            if (!CanConfront(p) || !SorceryDevelopment.Spend(p, 30)) return false;
            var r = SorceryDevelopment.State(p); r.nextConfrontation = SorceryDevelopment.Now + 60000;
            var v = HediffComp_SukunaVessel.For(p);
            var influence = p.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_SukunaInfluence);
            float chance = UnityEngine.Mathf.Clamp(.55f + r.resolve * .05f - influence.Severity * .25f, .3f, .85f);
            if (Rand.Chance(chance))
            {
                r.resolve = System.Math.Min(5, r.resolve + 1); influence.Severity = System.Math.Max(.01f, influence.Severity - .05f);
                v.suppressedUntil = System.Math.Max(v.suppressedUntil, SorceryDevelopment.Now + 30000);
                Messages.Message("JJK_DeepConfrontWon".Translate(), p, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                influence.Severity = System.Math.Min(1, influence.Severity + .05f);
                v.suppressedUntil = 0; v.BeginPossession();
            }
            return true;
        }
        public static bool QuietBargain(Pawn p)
        {
            var v = HediffComp_SukunaVessel.For(p);
            if (v?.CanBargain != true || SorceryDevelopment.Has(p, "JJK_VesselQuietPrice")) return false;
            p.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_VesselQuietPrice"));
            v.suppressedUntil = SorceryDevelopment.Now + 180000; v.nextBargain = v.suppressedUntil;
            Messages.Message("JJK_DeepQuietMade".Translate(), p, MessageTypeDefOf.CautionInput); return true;
        }
        public static bool CanRescue(Pawn rescuer, Pawn vessel) => SorceryDevelopment.CanAct(rescuer)
            && rescuer != vessel && JJK_GradeUtility.IsSorcerer(rescuer) && vessel?.Spawned == true && !vessel.Dead
            && vessel.Faction == rescuer.Faction && vessel.Map == rescuer.Map && !vessel.InMentalState
            && SorceryDevelopment.State(vessel).rescueDeadline > SorceryDevelopment.Now && HediffComp_SukunaVessel.For(vessel) != null;
        public static bool Rescue(Pawn rescuer, Pawn vessel)
        {
            if (!CanRescue(rescuer, vessel) || rescuer.Position.DistanceToSquared(vessel.Position) > 4 || !SorceryDevelopment.Spend(rescuer, 40)) return false;
            var r = SorceryDevelopment.State(vessel); r.rescueDeadline = 0; r.rescues++;
            HediffComp_SukunaVessel.For(vessel).Suppress();
            vessel.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_VesselRescued"), rescuer);
            rescuer.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_VesselTrusted"), vessel);
            Messages.Message("JJK_DeepRescued".Translate(vessel.LabelShortCap, rescuer.LabelShortCap), vessel, MessageTypeDefOf.PositiveEvent); return true;
        }
    }
    public class JobDriver_InnerConfrontation : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !VesselDevelopment.CanConfront(pawn));
            var wait = Toils_General.Wait(600); wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            yield return Toils_General.Do(() => VesselDevelopment.FinishConfrontation(pawn));
        }
    }
    public class JobDriver_AnchorVessel : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !VesselDevelopment.CanRescue(pawn, TargetA.Pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                // A conscious vessel cooperates with the requested rescue instead of
                // wandering out of reach during its five-second ritual.
                var vessel = TargetA.Pawn;
                if (!vessel.Downed && !vessel.Drafted)
                {
                    var hold = JobMaker.MakeJob(JobDefOf.Wait, vessel.Position); hold.expiryInterval = 360;
                    vessel.jobs.TryTakeOrderedJob(hold);
                }
            });
            yield return Toils_General.Wait(300, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A)
                .FailOn(() => pawn.Position.DistanceToSquared(TargetA.Pawn.Position) > 4);
            yield return Toils_General.Do(() => VesselDevelopment.Rescue(pawn, TargetA.Pawn));
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetFloatMenuOptions))]
    public static class Patch_VesselAnchorMenu
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, Pawn __instance, Pawn selPawn)
        {
            foreach (var option in values) yield return option;
            if (VesselDevelopment.CanRescue(selPawn, __instance) && selPawn.CanReserveAndReach(__instance, PathEndMode.Touch, Danger.Deadly))
                yield return new FloatMenuOption("JJK_DeepAnchor".Translate(), () => selPawn.jobs.TryTakeOrderedJob(
                    JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AnchorVessel"), __instance)));
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_VesselHarmedRelationship
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt > 0 && dinfo.Instigator is Pawn vessel && vessel != __instance
                && HediffComp_SukunaVessel.For(vessel)?.Possessed == true)
                __instance.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_VesselHarmedMe"), vessel);
        }
    }
}
