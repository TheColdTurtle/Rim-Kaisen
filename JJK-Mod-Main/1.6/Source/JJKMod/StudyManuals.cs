using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class SorcererStudyRecord : IExposable
    {
        public Pawn pawn;
        public int gradeBooks, trialPasses, nextStudy, nextTrial;
        public Dictionary<AbilityTreeDef, int> masteryBooks = new Dictionary<AbilityTreeDef, int>();
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref gradeBooks, "gradeBooks"); Scribe_Values.Look(ref trialPasses, "trialPasses");
            Scribe_Values.Look(ref nextStudy, "nextStudy"); Scribe_Values.Look(ref nextTrial, "nextTrial");
            Scribe_Collections.Look(ref masteryBooks, "masteryBooks", LookMode.Def, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && masteryBooks == null) masteryBooks = new Dictionary<AbilityTreeDef, int>();
        }
    }
    public class GameComponent_SorcererStudy : GameComponent
    {
        public List<SorcererStudyRecord> records = new List<SorcererStudyRecord>();
        public GameComponent_SorcererStudy(Game game) { }
        public static GameComponent_SorcererStudy Instance => Current.Game.GetComponent<GameComponent_SorcererStudy>();
        public SorcererStudyRecord For(Pawn p)
        {
            var record = records.FirstOrDefault(r => r.pawn == p);
            if (record == null) { record = new SorcererStudyRecord { pawn = p }; records.Add(record); }
            return record;
        }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) records.RemoveAll(r => r.pawn == null || r.pawn.Destroyed);
            Scribe_Collections.Look(ref records, "studyRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { if (records == null) records = new List<SorcererStudyRecord>(); records.RemoveAll(r => r.pawn == null); }
        }
    }
    public static class StudyManuals
    {
        public const int StudyTicks = 1800, CooldownTicks = 180000;
        public static int GradeGain(Pawn p) => new[] { 75, 100, 150, 200, 0, 0 }[(int)GameComponent_JJK.Instance.GetGrade(p)];
        public static IEnumerable<AbilityTreeDef> OwnedTrees(Pawn p)
        {
            var ac = JujutsuTraining.Class(p);
            if (p?.genes == null || ac == null) return Enumerable.Empty<AbilityTreeDef>();
            return JJK_Utils.CursedTechniqueGenes.Where(g => p.genes.HasActiveGene(g))
                .SelectMany(g => g.GetModExtension<AbilityExtension>()?.abilityTrees ?? new List<AbilityTreeDef>())
                .Where(t => ac.UnlockedTrees.Contains(t)).Distinct();
        }
        public static int MasteryGain(Pawn p, AbilityTreeDef tree)
        {
            int count = tree != null && GameComponent_SorcererStudy.Instance.For(p).masteryBooks.TryGetValue(tree, out int n) ? n : 0;
            return count < 3 ? new[] { 250, 125, 60 }[count] : 0;
        }
        public static string Reason(Pawn p, bool mastery, AbilityTreeDef tree = null)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Drafted || p.Faction != Faction.OfPlayer
                || !JJK_GradeUtility.IsSorcerer(p)) return "JJK_ManualReader".Translate();
            var r = GameComponent_SorcererStudy.Instance.For(p);
            if (r.nextStudy > Find.TickManager.TicksGame) return "JJK_ManualRest".Translate((r.nextStudy - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            if (!mastery)
            {
                int grade = (int)GameComponent_JJK.Instance.GetGrade(p);
                if (grade >= 4) return "JJK_ManualGradeCap".Translate();
                if ((r.gradeBooks & (1 << grade)) != 0) return "JJK_ManualGradeUsed".Translate();
            }
            else if (tree == null ? !OwnedTrees(p).Any(t => MasteryGain(p, t) > 0) : !OwnedTrees(p).Contains(tree) || MasteryGain(p, tree) == 0)
                return "JJK_ManualNoTechnique".Translate();
            return null;
        }
        public static bool Complete(Pawn p, Thing book, AbilityTreeDef tree)
        {
            var comp = book?.TryGetComp<CompStudyManual>();
            if (comp == null || book.Destroyed || !book.Spawned || p?.Map != book.Map
                || p.Position.DistanceToSquared(book.Position) > 2 || Reason(p, comp.Props.mastery, tree) != null
                || (comp.Props.mastery && tree == null)) return false;
            var r = GameComponent_SorcererStudy.Instance.For(p);
            int amount;
            if (comp.Props.mastery)
            {
                amount = MasteryGain(p, tree); r.masteryBooks.TryGetValue(tree, out int n); r.masteryBooks[tree] = n + 1;
                GameComponent_JJK.Instance.AddTechniqueXp(p, tree, amount);
            }
            else
            {
                amount = GradeGain(p); r.gradeBooks |= 1 << (int)GameComponent_JJK.Instance.GetGrade(p);
                GameComponent_JJK.Instance.AddGradeXp(p, amount);
            }
            r.nextStudy = Find.TickManager.TicksGame + CooldownTicks;
            book.SplitOff(1).Destroy();
            Messages.Message("JJK_ManualStudied".Translate(p.LabelShortCap), p, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }
        public static void Start(Pawn p, Thing book, AbilityTreeDef tree)
        {
            var comp = book?.TryGetComp<CompStudyManual>();
            if (comp == null || Reason(p, comp.Props.mastery, tree) != null || !book.Spawned || book.Map != p.Map
                || book.IsForbidden(p) || !p.CanReserveAndReach(book, PathEndMode.Touch, Danger.Some)) return;
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_StudyManual"), book);
            // RimWorld creates a fresh driver when the job actually starts. Keep
            // the selection on the reserved book until that driver claims it.
            comp.reader = p; comp.selectedTree = tree;
            p.jobs.TryTakeOrderedJob(job);
        }
    }
    public class CompProperties_StudyManual : CompProperties
    {
        public bool mastery;
        public CompProperties_StudyManual() { compClass = typeof(CompStudyManual); }
    }
    public class CompStudyManual : ThingComp
    {
        public Pawn reader;
        public AbilityTreeDef selectedTree;
        public override void PostExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && reader?.Destroyed == true) reader = null;
            Scribe_References.Look(ref reader, "manualReader"); Scribe_Defs.Look(ref selectedTree, "manualTree");
        }
        public CompProperties_StudyManual Props => (CompProperties_StudyManual)props;
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn p)
        {
            string reason = StudyManuals.Reason(p, Props.mastery);
            if (reason == null && (parent.IsForbidden(p) || !p.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))) reason = "NoPath".Translate();
            if (reason != null) { yield return new FloatMenuOption("JJK_ManualCannot".Translate(reason), null); yield break; }
            yield return new FloatMenuOption("JJK_ManualStudy".Translate(parent.LabelCap), () =>
            {
                if (Props.mastery) Find.WindowStack.Add(new Window_StudyTechnique(p, parent));
                else StudyManuals.Start(p, parent, null);
            });
        }
    }
    public class Window_StudyTechnique : Window
    {
        private readonly Pawn pawn; private readonly Thing book; private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(530, 460);
        public Window_StudyTechnique(Pawn p, Thing book) { pawn = p; this.book = book; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            if (book.Destroyed || pawn.Destroyed) { Close(); return; }
            var trees = StudyManuals.OwnedTrees(pawn).Where(t => StudyManuals.MasteryGain(pawn, t) > 0).ToList();
            Widgets.Label(new Rect(0, 0, rect.width, 65), "JJK_ManualChoose".Translate());
            var view = new Rect(0, 0, rect.width - 20, trees.Count * 48);
            Widgets.BeginScrollView(new Rect(0, 70, rect.width, rect.height - 70), ref scroll, view);
            for (int i = 0; i < trees.Count; i++)
                if (Widgets.ButtonText(new Rect(0, i * 48, view.width, 42), trees[i].LabelCap + " +" + StudyManuals.MasteryGain(pawn, trees[i])))
                { StudyManuals.Start(pawn, book, trees[i]); Close(); }
            Widgets.EndScrollView();
        }
    }
    public class JobDriver_StudyManual : JobDriver
    {
        public AbilityTreeDef tree;
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            var comp = TargetThingA?.TryGetComp<CompStudyManual>();
            if (comp?.reader == pawn) { tree = comp.selectedTree; comp.reader = null; comp.selectedTree = null; }
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Defs.Look(ref tree, "studyTree"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => StudyManuals.Reason(pawn, TargetThingA?.TryGetComp<CompStudyManual>()?.Props.mastery ?? false, tree) != null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(StudyManuals.StudyTicks, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A)
                .FailOn(() => pawn.Position.DistanceToSquared(TargetThingA.Position) > 2);
            yield return Toils_General.Do(() => StudyManuals.Complete(pawn, TargetThingA, tree));
        }
    }
}
