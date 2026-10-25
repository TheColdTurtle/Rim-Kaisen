using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class SchoolStudent : IExposable
    {
        public Pawn pawn, mentor;
        public int lessons, missions, lastLessonDay = -1;
        public bool graduated;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "student"); Scribe_References.Look(ref mentor, "mentor");
            Scribe_Values.Look(ref lessons, "lessons"); Scribe_Values.Look(ref missions, "missions");
            Scribe_Values.Look(ref lastLessonDay, "lastLessonDay", -1); Scribe_Values.Look(ref graduated, "graduated");
        }
    }
    public class ClanInterest : IExposable
    {
        public string name;
        public int standing, deadline, nextDemand, demand;
        public Pawn candidate, leader;
        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name"); Scribe_Values.Look(ref standing, "standing");
            Scribe_Values.Look(ref deadline, "deadline"); Scribe_Values.Look(ref nextDemand, "nextDemand"); Scribe_Values.Look(ref demand, "demand");
            Scribe_References.Look(ref candidate, "candidate"); Scribe_References.Look(ref leader, "leader");
        }
        public string DemandText => demand == 0 ? "Recruitment demand: release " + (candidate?.LabelShortCap ?? "the nominated sorcerer") + " to the clan, or refuse. Acceptance permanently transfers the pawn."
            : demand == 1 ? "Inheritance dispute: pay 250 silver to recognize this clan's relic claim. Gain a sealed relic and standing; rival clans object."
            : "Leadership pressure: appoint " + (candidate?.LabelShortCap ?? "the nominated sorcerer") + " as the colony's clan representative. They stay in your colony; competing clans lose standing.";
    }
    public class GameComponent_ColonyLife : GameComponent, IThingHolder
    {
        public List<SchoolStudent> students = new List<SchoolStudent>();
        public List<ClanInterest> clans = new List<ClanInterest>();
        public List<RivalSorcerer> rivals = new List<RivalSorcerer>();
        private ThingOwner<Pawn> returningRivals;
        public Pawn instructor;
        public Map instructorHome;
        public int instructorLeaves, nextInstructor, nextIntake, nextRival;
        public static GameComponent_ColonyLife Instance => Current.Game?.GetComponent<GameComponent_ColonyLife>();
        public GameComponent_ColonyLife(Game game) { returningRivals = new ThingOwner<Pawn>(this); }
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => returningRivals;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, returningRivals);
        public ThingOwner<Pawn> HeldRivals => returningRivals;
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                students.RemoveAll(s => s.pawn == null || s.pawn.Destroyed);
                foreach (var s in students) if (s.mentor?.Destroyed == true) s.mentor = null;
                foreach (var c in clans)
                {
                    if (c.candidate?.Destroyed == true) c.candidate = null;
                    if (c.leader?.Destroyed == true) c.leader = null;
                }
                foreach (var r in rivals) if (r.pawn?.Destroyed == true) { r.pawn = null; r.ended = true; }
                foreach (var r in rivals) if (r.rememberedOpponent?.Destroyed == true) r.rememberedOpponent = null;
            }
            Scribe_Collections.Look(ref students, "jujutsuStudents", LookMode.Deep);
            Scribe_Collections.Look(ref clans, "clanInterests", LookMode.Deep);
            Scribe_Collections.Look(ref rivals, "recurringRivals", LookMode.Deep);
            Scribe_Deep.Look(ref returningRivals, "returningRivals", this);
            Scribe_References.Look(ref instructor, "visitingInstructor"); Scribe_References.Look(ref instructorHome, "instructorHome");
            Scribe_Values.Look(ref instructorLeaves, "instructorLeaves"); Scribe_Values.Look(ref nextInstructor, "nextInstructor");
            Scribe_Values.Look(ref nextIntake, "nextIntake"); Scribe_Values.Look(ref nextRival, "nextRival");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                students = students ?? new List<SchoolStudent>(); clans = clans ?? new List<ClanInterest>(); rivals = rivals ?? new List<RivalSorcerer>();
                returningRivals = returningRivals ?? new ThingOwner<Pawn>(this);
                students.RemoveAll(s => s.pawn == null);
            }
        }
        public Building Classroom(Map map) => map?.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_SchoolDesk"))
            .OfType<Building>().FirstOrDefault(b => b.Faction == Faction.OfPlayer);
        public bool Enroll(Pawn p)
        {
            if (!ColonyLifeUtility.Available(p) || students.Any(s => s.pawn == p) || students.Count(s => !s.graduated && s.pawn?.Dead == false) >= 8) return false;
            students.Add(new SchoolStudent { pawn = p }); return true;
        }
        public bool Intake(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (Classroom(map) == null || now < nextIntake || students.Count(s => !s.graduated && s.pawn?.Dead == false) >= 8
                || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Animal)) return false;
            if (!ColonyLifeUtility.Pay(map, ThingDefOf.Silver, 150)) return false;
            // Regular generation keeps talent random; the school cannot choose or grant a technique.
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(p, entry, map); Enroll(p); nextIntake = now + 600000;
            Find.LetterStack.ReceiveLetter("New jujutsu student", p.LabelShortCap + " has joined the colony and enrolled. Innate talent is random; students without cursed energy can train support skills.", LetterDefOf.PositiveEvent, p);
            return true;
        }
        public bool Invite(Map map)
        {
            int now = Find.TickManager.TicksGame;
            Faction school = GameComponent_Jujutsu.Schools.FirstOrDefault(f => !f.HostileTo(Faction.OfPlayer));
            if (instructor != null || now < nextInstructor || Classroom(map) == null || school == null
                || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Animal)) return false;
            if (!ColonyLifeUtility.Pay(map, ThingDefOf.Silver, 200)) return false;
            instructor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_Grade1Sorcerer"), school,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(instructor, entry, map); instructorHome = map;
            instructorLeaves = now + 180000; nextInstructor = now + 600000;
            InstructorWait(); return true;
        }
        private void InstructorWait()
        {
            var desk = Classroom(instructor.Map);
            if (desk == null) return;
            instructor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_VisitingInstructor"), desk));
        }
        public bool Teacher(Pawn p, Map map) => p?.Spawned == true && p.Map == map && !p.Dead && !p.Downed && !p.Drafted && !p.InMentalState
            && (p.Faction == Faction.OfPlayer || p == instructor && Find.TickManager.TicksGame < instructorLeaves && !p.HostileTo(Faction.OfPlayer))
            && JJK_GradeUtility.IsSorcerer(p) && GameComponent_JJK.Instance.GetGrade(p) >= SorcererGrade.Grade2;
        public bool StartClass(Map map, Pawn teacher)
        {
            var desk = Classroom(map); int day = Find.TickManager.TicksGame / 60000;
            if (desk == null || !Teacher(teacher, map) || !teacher.CanReach(desk, PathEndMode.Touch, Danger.Some)
                || teacher.CurJob?.def == DefDatabase<JobDef>.GetNamed("JJK_TeachClass")) return false;
            var cohort = students.Where(s => !s.graduated && s.pawn != teacher && ColonyLifeUtility.Available(s.pawn) && !s.pawn.Drafted
                && s.pawn.Map == map && s.lastLessonDay != day && s.pawn.CanReach(desk, PathEndMode.Touch, Danger.Some)).ToList();
            if (cohort.Count == 0) return false;
            teacher.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_TeachClass"), desk));
            foreach (var s in cohort)
            {
                if (teacher.Faction == Faction.OfPlayer) s.mentor = teacher;
                s.pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AttendClass"), desk, teacher));
            }
            return true;
        }
        public bool FinishLesson(Pawn p, Pawn teacher, Thing desk)
        {
            var s = students.FirstOrDefault(x => x.pawn == p); int day = Find.TickManager.TicksGame / 60000;
            if (s == null || s.graduated || s.lastLessonDay == day || !Teacher(teacher, p.Map) || teacher.Position.DistanceToSquared(desk.Position) > 64
                || teacher.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_TeachClass")) return false;
            s.lastLessonDay = day; s.lessons++;
            p.skills?.Learn(SkillDefOf.Intellectual, 300); p.skills?.Learn(SkillDefOf.Medicine, 150);
            if (JJK_GradeUtility.IsSorcerer(p))
            {
                var plan = GameComponent_Jujutsu.Instance.Plan(p); plan.RefreshDay();
                int gain = Mathf.Min(20, 80 - plan.dailyXp);
                var tree = JujutsuTraining.Class(p)?.UnlockedTrees.FirstOrDefault();
                if (gain > 0 && tree != null) { GameComponent_JJK.Instance.AddTechniqueXp(p, tree, gain); plan.dailyXp += gain; }
                GameComponent_JJK.Instance.AddGradeXp(p, 10);
            }
            return true;
        }
        public void TrackSupervision(JujutsuContract c)
        {
            if (!c.expedition || c.site?.HasMap != true || c.siteCleared || c.participants.Count == 0) return;
            foreach (var s in students.Where(s => !s.graduated && ColonyLifeUtility.Available(s.pawn) && s.pawn.Map == c.site.Map
                && s.mentor != null && s.mentor != instructor && ColonyLifeUtility.Available(s.mentor) && s.mentor.Map == c.site.Map
                && s.pawn.Position.DistanceToSquared(s.mentor.Position) <= 400))
                if (!c.supervisedStudents.Contains(s.pawn)) { c.supervisedStudents.Add(s.pawn); c.supervisedMentors.Add(s.mentor); }
        }
        public void CompleteMission(JujutsuContract c)
        {
            foreach (var s in students.Where(s => !s.graduated && ExorcismExpeditions.Returned(s.pawn, c)))
            {
                int index = c.supervisedStudents.IndexOf(s.pawn);
                if (index >= 0 && index < c.supervisedMentors.Count && ExorcismExpeditions.Returned(c.supervisedMentors[index], c)) s.missions++;
            }
        }
        public bool Graduate(SchoolStudent s)
        {
            if (s == null || s.graduated || s.lessons < 5 || s.missions < 2 || !ColonyLifeUtility.Available(s.pawn) || Classroom(s.pawn.Map) == null) return false;
            s.graduated = true; GameComponent_JJK.Instance.AddGradeXp(s.pawn, 75);
            foreach (var f in GameComponent_Jujutsu.Schools.Where(f => !f.HostileTo(Faction.OfPlayer)))
                GameComponent_Jujutsu.Instance.Standing(f).reputation = Mathf.Min(100, GameComponent_Jujutsu.Instance.Standing(f).reputation + 5);
            Find.LetterStack.ReceiveLetter("Jujutsu graduation", s.pawn.LabelShortCap + " completed five lesson days and two supervised expeditions. They remain in the colony and earn 75 grade XP if a sorcerer.", LetterDefOf.PositiveEvent, s.pawn);
            return true;
        }
        public void EnsureClans()
        {
            if (clans.Count == 0) foreach (string name in new[] { "Gojo", "Zenin", "Kamo" })
                clans.Add(new ClanInterest { name = name, nextDemand = Find.TickManager.TicksGame + Rand.Range(300000, 600000) });
        }
        public bool Resolve(ClanInterest clan, Map map, bool accept)
        {
            if (clan.deadline == 0 || Find.TickManager.TicksGame >= clan.deadline || map?.IsPlayerHome != true) return false;
            if (accept && clan.demand != 1 && (!ColonyLifeUtility.Available(clan.candidate) || clan.candidate.Map != map)) return false;
            if (accept && clan.demand == 0)
            {
                var faction = GameComponent_Jujutsu.Schools.FirstOrDefault(f => !f.HostileTo(Faction.OfPlayer));
                if (faction == null) return false;
                Pawn p = clan.candidate; p.SetFaction(faction); p.jobs.StopAll();
                SendAway(p);
            }
            if (accept && clan.demand == 1)
            {
                if (!ColonyLifeUtility.Pay(map, ThingDefOf.Silver, 250)) return false;
                var relic = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_DangerousRelic"));
                GenPlace.TryPlaceThing(relic, map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center, map, ThingPlaceMode.Near);
            }
            if (accept && clan.demand == 2) clan.leader = clan.candidate;
            clan.standing = Mathf.Clamp(clan.standing + (accept ? 15 : -10), -100, 100);
            if (accept) foreach (var rival in clans.Where(c => c != clan)) rival.standing = Mathf.Max(-100, rival.standing - 5);
            clan.deadline = 0; clan.candidate = null; clan.nextDemand = Find.TickManager.TicksGame + 600000;
            return true;
        }
        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;
            if (instructor != null)
            {
                if (instructor.Dead || instructor.Destroyed || !instructor.Spawned || instructor.Faction == Faction.OfPlayer || instructor.HostileTo(Faction.OfPlayer)) instructor = null;
                else if (now >= instructorLeaves)
                {
                    if (!instructor.Downed && SendAway(instructor)) instructor = null;
                }
                else if (instructor.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_TeachClass") && instructor.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_VisitingInstructor")) InstructorWait();
            }
            EnsureClans();
            if (now % 2500 == 0)
            {
                var candidates = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.mapPawns.FreeColonistsSpawned).Where(JJK_GradeUtility.IsSorcerer).ToList();
                foreach (var clan in clans)
                {
                    if (clan.leader != null && (clan.leader.Dead || clan.leader.Destroyed || clan.leader.Faction != Faction.OfPlayer)) clan.leader = null;
                    if (clan.deadline > 0 && now >= clan.deadline) { clan.standing = Mathf.Max(-100, clan.standing - 10); clan.deadline = 0; clan.candidate = null; clan.nextDemand = now + 600000; }
                    if (clan.deadline == 0 && now >= clan.nextDemand && candidates.Count > 0)
                    {
                        clan.demand = Rand.Range(0, 3); clan.candidate = candidates.OrderByDescending(p => GameComponent_JJK.Instance.GetGradeXp(p)).First(); clan.deadline = now + 180000;
                        Find.LetterStack.ReceiveLetter(clan.name + " clan petition", clan.DemandText + " Respond within three days in Colony life. Silence costs 10 standing.", LetterDefOf.NeutralEvent, clan.candidate);
                    }
                }
            }
            RivalWorld.Tick(this, now);
        }
        public static bool SendAway(Pawn p)
        {
            if (p?.Spawned != true || !CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(p.Map) && p.CanReach(c, PathEndMode.OnCell, Danger.Deadly),
                p.Map, CellFinder.EdgeRoadChance_Ignore, out IntVec3 exit)) return false;
            var job = JobMaker.MakeJob(JobDefOf.Goto, exit); job.exitMapOnArrival = true;
            p.jobs.TryTakeOrderedJob(job); return true;
        }
    }
    public class JobDriver_TeachClass : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !GameComponent_ColonyLife.Instance.Teacher(pawn, pawn.Map));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(4200, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
        }
    }
    public class JobDriver_AttendClass : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A); this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => !ColonyLifeUtility.Available(pawn) || pawn.Drafted || !GameComponent_ColonyLife.Instance.Teacher(TargetB.Pawn, pawn.Map)
                || TargetB.Pawn.CurJob?.def != DefDatabase<JobDef>.GetNamed("JJK_TeachClass"));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600, TargetIndex.A);
            var study = Toils_General.Wait(1800, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            study.AddPreTickAction(() => { if (TargetB.Pawn.Position.DistanceToSquared(TargetA.Cell) > 64) EndJobWith(JobCondition.Incompletable); });
            yield return study;
            yield return Toils_General.Do(() => GameComponent_ColonyLife.Instance.FinishLesson(pawn, TargetB.Pawn, TargetA.Thing));
        }
    }
    public class JobDriver_VisitingInstructor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(2500, TargetIndex.A);
        }
    }
    public class Window_ColonyLife : Window
    {
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(850, 740);
        public Window_ColonyLife() { doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            var gc = GameComponent_ColonyLife.Instance; Map map = Find.CurrentMap;
            if (gc == null) return;
            var view = new Rect(0, 0, rect.width - 24, 1800 + gc.students.Count * 130 + gc.rivals.Count * 100);
            Widgets.BeginScrollView(rect, ref scroll, view); var list = new Listing_Standard(); list.Begin(view);
            list.Label("Jujutsu school");
            list.Label("Build a school desk. Group classes require a Grade 2+ teacher and count once per student per day. Graduation needs five lesson days and two supervised expeditions. Research work maintains vaults; order school classes here.");
            if (map?.IsPlayerHome == true)
            {
                if (list.ButtonText("Student intake — 150 silver; once per 10 days; random innate talent"))
                    if (!gc.Intake(map)) ColonyLifeUtility.Reject("Requires a school desk, 150 silver in the home area, an open student slot (8 maximum), and the intake cooldown to have ended.");
                if (gc.nextIntake > Find.TickManager.TicksGame) list.Label("Next intake: " + (gc.nextIntake - Find.TickManager.TicksGame).ToStringTicksToPeriod());
                if (list.ButtonText("Enroll a colonist")) Find.WindowStack.Add(new FloatMenu(map.mapPawns.FreeColonistsSpawned
                    .Where(p => !gc.students.Any(s => s.pawn == p)).Select(p => new FloatMenuOption(p.LabelShortCap, () => gc.Enroll(p))).ToList()));
                if (list.ButtonText("Invite instructor — 200 silver; three-day visit; once per 10 days"))
                    if (!gc.Invite(map)) ColonyLifeUtility.Reject("Requires a school desk, a friendly school, 200 silver in the home area, and no active visit or cooldown.");
                if (list.ButtonText("Start group lesson — choose teacher"))
                {
                    var teachers = map.mapPawns.AllPawnsSpawned.Where(p => gc.Teacher(p, map)).ToList();
                    if (teachers.Count == 0) ColonyLifeUtility.Reject("A conscious, undrafted Grade 2+ teacher must be present.");
                    else Find.WindowStack.Add(new FloatMenu(teachers.Select(p => new FloatMenuOption(p.LabelShortCap, () =>
                    { if (!gc.StartClass(map, p)) ColonyLifeUtility.Reject("No eligible students, no reachable school desk, or class already in progress."); })).ToList()));
                }
            }
            if (gc.instructor != null) list.Label("Visiting instructor: " + gc.instructor.LabelShortCap + " — " + Mathf.Max(0, gc.instructorLeaves - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            foreach (var s in gc.students)
            {
                list.Label((s.pawn?.LabelShortCap ?? "Lost student") + " — " + (s.graduated ? "Graduated" : s.lessons + "/5 lesson days; " + s.missions + "/2 supervised missions") + " — mentor: " + (s.mentor?.LabelShortCap ?? "unassigned"));
                if (!s.graduated && s.pawn?.Dead == false && map != null && list.ButtonText("Assign field mentor for " + s.pawn.LabelShortCap))
                    Find.WindowStack.Add(new FloatMenu(map.mapPawns.FreeColonistsSpawned.Where(p => p != s.pawn && gc.Teacher(p, map))
                        .Select(p => new FloatMenuOption(p.LabelShortCap, () => s.mentor = p)).ToList()));
                if (!s.graduated && s.lessons >= 5 && s.missions >= 2 && list.ButtonText("Graduate " + s.pawn.LabelShortCap)) gc.Graduate(s);
            }
            list.GapLine(); list.Label("Clan politics"); gc.EnsureClans();
            list.Label("Clans compete for recruits, relic inheritance and representatives. Accepting one clan's petition costs 5 standing with each other clan. At -30 standing, rivals receive extra support; at +30, rivals face weaker support.");
            foreach (var clan in gc.clans)
            {
                list.Label(clan.name + ": " + clan.standing + " standing — representative: " + (clan.leader?.LabelShortCap ?? "none"));
                if (clan.deadline == 0) continue;
                list.Label(clan.DemandText + " Time left: " + Mathf.Max(0, clan.deadline - Find.TickManager.TicksGame).ToStringTicksToPeriod());
                if (map?.IsPlayerHome == true && list.ButtonText("Accept " + clan.name + " petition"))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(clan.DemandText, () => { if (!gc.Resolve(clan, map, true)) ColonyLifeUtility.Reject("The petition expired, its candidate is unavailable, or the required silver is missing."); }));
                if (map?.IsPlayerHome == true && list.ButtonText("Refuse " + clan.name + " petition (-10 standing)")) gc.Resolve(clan, map, false);
            }
            list.GapLine(); list.Label("Recurring rivals");
            foreach (var r in gc.rivals) list.Label(r.Description);
            if (gc.rivals.Count == 0) list.Label("Rivals can challenge established colonies after day 20. A rival who escapes returns with improved mastery and different tactics. Death or recruitment ends their rivalry.");
            list.End(); Widgets.EndScrollView();
        }
    }
}
