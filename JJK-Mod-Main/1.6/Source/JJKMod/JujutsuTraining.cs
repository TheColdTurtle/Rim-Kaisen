using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public class TrainingPlan : IExposable
    {
        public Pawn pupil, mentor;
        public AbilityTreeDef tree;
        public AbilityDef lesson;
        public bool enabled;
        public int hour = 10, day = -1, dailyXp, lessonProgress;
        public void ExposeData()
        {
            Scribe_References.Look(ref pupil, "pupil"); Scribe_References.Look(ref mentor, "mentor");
            Scribe_Defs.Look(ref tree, "tree"); Scribe_Defs.Look(ref lesson, "lesson");
            Scribe_Values.Look(ref enabled, "enabled"); Scribe_Values.Look(ref hour, "hour", 10);
            Scribe_Values.Look(ref day, "day", -1); Scribe_Values.Look(ref dailyXp, "dailyXp");
            Scribe_Values.Look(ref lessonProgress, "lessonProgress");
        }
        public bool Scheduled => pupil != null && (GenLocalDate.HourInteger(pupil) - hour + 24) % 24 < 2;
        public void RefreshDay()
        {
            int today = Find.TickManager.TicksGame / 60000;
            if (today != day) { day = today; dailyXp = 0; }
        }
    }

    public static class JujutsuTraining
    {
        public static AbilityClass Class(Pawn p) => p?.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
        public static List<CompProperties_LearnAbilityScroll> Lessons => DefDatabase<ThingDef>.AllDefsListForReading
            .Select(d => d.GetCompProperties<CompProperties_LearnAbilityScroll>()).Where(c => c != null).ToList();
        public static bool MentorReady(TrainingPlan plan) => plan.mentor != null && plan.mentor != plan.pupil
            && plan.mentor.Spawned && plan.mentor.Map == plan.pupil.Map && !plan.mentor.Dead
            && !plan.mentor.Downed && !plan.mentor.Drafted && !plan.mentor.InMentalState
            && plan.mentor.Faction == plan.pupil.Faction && JJK_GradeUtility.IsSorcerer(plan.mentor);
        public static bool Valid(TrainingPlan plan, bool schedule = true)
        {
            if (plan?.pupil == null || !plan.enabled || plan.tree == null || !plan.pupil.Spawned
                || plan.pupil.Downed || plan.pupil.Dead || plan.pupil.Drafted || plan.pupil.InMentalState
                || plan.pupil.Faction != Faction.OfPlayer || !JJK_GradeUtility.IsSorcerer(plan.pupil)) return false;
            plan.RefreshDay();
            if (plan.dailyXp >= 80 || (schedule && !plan.Scheduled)) return false;
            if (plan.mentor != null && !MentorReady(plan)) return false;
            var ac = Class(plan.pupil);
            if (ac == null) return false;
            if ((plan.pupil.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy)?.energy ?? 0f) < 10f) return false;
            if (plan.lesson != null)
                return MentorReady(plan) && Lessons.Any(l => l.abilityToLearn == plan.lesson && l.tree == plan.tree)
                    && Class(plan.mentor)?.GetLearnedAbility(plan.lesson) != null
                    && ac.GetLearnedAbility(plan.lesson) == null;
            return ac.UnlockedTrees.Contains(plan.tree);
        }
        public static int Complete(TrainingPlan plan)
        {
            if (!Valid(plan, false)) return 0;
            var ce = plan.pupil.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (ce == null || ce.energy < 10f) return 0;
            if (plan.mentor != null && plan.pupil.Position.DistanceToSquared(plan.mentor.Position) > 25) return 0;
            ce.energy -= 10f;
            int mastery = GameComponent_JJK.Instance.GetTechniqueXp(plan.pupil, plan.tree);
            bool expert = MentorReady(plan) && GameComponent_JJK.Instance.GetTechniqueXp(plan.mentor, plan.tree) >= mastery + 200;
            int gain = System.Math.Min(80 - plan.dailyXp, System.Math.Max(1, (expert ? 30 : 20) / (1 + mastery / 250)));
            plan.dailyXp += gain;
            if (plan.lesson != null)
            {
                plan.lessonProgress += gain;
                if (plan.lessonProgress >= 100)
                {
                    Class(plan.pupil).UnlockTree(plan.tree);
                    Class(plan.pupil).LearnAbility(plan.lesson, false);
                    Messages.Message("JJK_LessonComplete".Translate(plan.pupil.LabelShortCap, plan.lesson.LabelCap), plan.pupil, MessageTypeDefOf.PositiveEvent);
                    plan.lesson = null; plan.lessonProgress = 0;
                }
            }
            else GameComponent_JJK.Instance.AddTechniqueXp(plan.pupil, plan.tree, gain);
            return gain;
        }
    }

    public class WorkGiver_JujutsuTraining : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("JJK_TrainingPost"));
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var plan = GameComponent_Jujutsu.Instance.Plan(pawn);
            return JujutsuTraining.Valid(plan) && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some)
                && (plan.mentor == null || pawn.CanReserve(plan.mentor)
                    && plan.mentor.CanReach(t, PathEndMode.Touch, Danger.Some)
                    && plan.mentor.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_Practice"));
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
            => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_Practice"), t, GameComponent_Jujutsu.Instance.Plan(pawn).mentor);
    }

    public class JobDriver_JujutsuPractice : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed)
            && (!job.targetB.IsValid || pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed));
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !JujutsuTraining.Valid(GameComponent_Jujutsu.Instance.Plan(pawn), false));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                Pawn mentor = job.targetB.Pawn;
                if (mentor != null) mentor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_Mentor"), pawn));
            });
            var practice = Toils_General.Wait(1800, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            practice.AddPreTickAction(() =>
            {
                if (job.targetB.IsValid && job.targetB.Pawn?.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_Mentor"))
                    EndJobWith(JobCondition.Incompletable);
            });
            yield return practice;
            yield return Toils_General.Do(() => JujutsuTraining.Complete(GameComponent_Jujutsu.Instance.Plan(pawn)));
        }
    }
    public class JobDriver_JujutsuMentor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => TargetA.Pawn?.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_Practice"));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(2400, TargetIndex.A);
        }
    }
}
