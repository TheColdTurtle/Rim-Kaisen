using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_JujutsuDossierGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var value in values) yield return value;
            if (__instance.Faction == Faction.OfPlayer && JJK_GradeUtility.IsSorcerer(__instance))
                yield return new Command_Action { defaultLabel = "JJK_Dossier".Translate(),
                    defaultDesc = "JJK_DossierDesc".Translate(), icon = TexCommand.ClearPrioritizedWork,
                    action = () => Find.WindowStack.Add(new Window_JujutsuDossier(__instance)) };
        }
    }
    public class MainTabWindow_Jujutsu : MainTabWindow
    {
        private Vector2 scroll;
        private readonly Dictionary<Faction, int> selectedTiers = new Dictionary<Faction, int>();
        public override Vector2 InitialSize => new Vector2(980, 730);
        public override void DoWindowContents(Rect rect)
        {
            var gc = GameComponent_Jujutsu.Instance;
            if (gc == null) return;
            var view = new Rect(0, 0, rect.width - 22, 2100 + gc.contracts.Count * 460);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_Headquarters".Translate());
            list.Label("JJK_HeadquartersDesc".Translate()); list.GapLine();
            if (list.ButtonText("JJK_CullingOpen".Translate())) Find.WindowStack.Add(new Window_Kogane());
            if (list.ButtonText("JJK_RecordTitle".Translate())) Find.WindowStack.Add(new Window_CampaignHistory());
            if (list.ButtonText("JJK_TrialOpen".Translate())) Find.WindowStack.Add(new Window_PromotionTrials());
            if (list.ButtonText("Colony life — school, clans and rivals")) Find.WindowStack.Add(new Window_ColonyLife());
            foreach (Pawn p in Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsSpawned).Where(JJK_GradeUtility.IsSorcerer))
                if (list.ButtonText(p.LabelShortCap + " — " + JJK_GradeUtility.GradeLabels[(int)GameComponent_JJK.Instance.GetGrade(p)]))
                    Find.WindowStack.Add(new Window_JujutsuDossier(p));
            list.GapLine(); list.Label("JJK_Contracts".Translate());
            foreach (Faction school in GameComponent_Jujutsu.Schools)
            {
                var s = gc.Standing(school);
                list.Label(school.def.LabelCap + " (" + school.Name + ") — " + "JJK_Reputation".Translate(s.reputation, s.MaxTier));
                if (school.HostileTo(Faction.OfPlayer)) { list.Label("JJK_SchoolHostile".Translate()); continue; }
                if (s.nextOfferTick > Find.TickManager.TicksGame)
                    list.Label("JJK_OffersReturn".Translate((s.nextOfferTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
                int chosenTier = selectedTiers.TryGetValue(school, out int savedTier) ? Mathf.Min(savedTier, s.MaxTier) : s.MaxTier;
                if (list.ButtonText("JJK_ContractTier".Translate(chosenTier)))
                    Find.WindowStack.Add(new FloatMenu(Enumerable.Range(1, s.MaxTier).Select(t =>
                        new FloatMenuOption("JJK_ContractTier".Translate(t), () => selectedTiers[school] = t)).ToList()));
                foreach (ContractKind kind in new[] { ContractKind.Infestation, ContractKind.Rescue, ContractKind.Recovery, ContractKind.Boss })
                {
                    int tier = kind == ContractKind.Boss ? 3 : chosenTier;
                    Map map = Find.CurrentMap;
                    if (!gc.CanAccept(school, map, tier, kind)) continue;
                    if (list.ButtonText("JJK_AcceptContract".Translate(("JJK_Contract" + kind).Translate(), tier)))
                    {
                        Faction giver = school; ContractKind selected = kind;
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_ContractConfirm".Translate(tier,
                            gc.RewardFor(giver, tier).LabelCap, tier == 1 ? 60 : 1),
                            () => gc.Accept(giver, map, tier, selected)));
                    }
                }
                if (gc.CanAccept(school, Find.CurrentMap, 3, ContractKind.Investigation))
                {
                    list.Label("JJK_SchoolBrief".Translate());
                    if (list.ButtonText("JJK_SchoolCampaign".Translate())) SchoolCampaign.Begin(school, Find.CurrentMap);
                }
                foreach (ContractKind kind in new[] { ContractKind.Infestation, ContractKind.Rescue, ContractKind.Recovery, ContractKind.Investigation, ContractKind.Evacuation, ContractKind.Haunting, ContractKind.Sealing })
                {
                    Map home = Find.CurrentMap;
                    if (kind == ContractKind.Sealing && chosenTier != 3) continue;
                    if (!gc.CanAccept(school, home, chosenTier, kind)) continue;
                    if (list.ButtonText("JJK_ExpeditionAccept".Translate(("JJK_Contract" + kind).Translate(), chosenTier)))
                    {
                        Faction giver = school; ContractKind selected = kind; int tier = chosenTier;
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_ExpeditionConfirm".Translate(
                            gc.RewardFor(giver, tier).LabelCap, tier == 1 ? 60 : 1, home.Parent.LabelCap),
                            () => ExorcismExpeditions.Accept(giver, home, tier, selected)));
                    }
                }
                list.Gap();
            }
            foreach (var c in gc.contracts.ToList())
            {
                list.Label(c.Title + " — " + ("JJK_ContractState" + c.state).Translate());
                if (c.state == ContractState.Completed || c.state == ContractState.Failed) continue;
                list.Label("JJK_ContractProgress".Translate(c.Remaining, Mathf.Max(0, c.expires - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
                SchoolCampaign.Draw(list, c); SealingCampaign.Draw(list, c);
                if (c.promotionTrial) list.Label("JJK_TrialProgress".Translate(c.trialCandidate?.LabelShortCap ?? "?", c.trialEngaged.Count, c.trialRequired));
                if (c.expedition)
                {
                    list.Label(ExorcismExpeditions.Progress(c));
                    if (c.kind == ContractKind.Rescue && c.siteCleared && c.rescue != null && c.rescue.Faction != Faction.OfPlayer
                        && list.ButtonText("JJK_ExpeditionRecruit".Translate())) ExorcismExpeditions.Recruit(c);
                }
                else
                {
                    if (c.kind == ContractKind.Rescue) list.Label("JJK_RescueObjective".Translate());
                    if (c.kind == ContractKind.Recovery) list.Label("JJK_RecoveryObjective".Translate());
                }
                if (list.ButtonText("JJK_LocateContract".Translate()))
                {
                    if (c.expedition && c.site != null && !c.site.Destroyed) { CameraJumper.TryJumpAndSelect(c.site); continue; }
                    Thing target = c.rescue ?? c.artifact ?? (Thing)c.threats.FirstOrDefault(p => p != null && p.Spawned && !p.Dead);
                    if (target != null) CameraJumper.TryJumpAndSelect(target);
                }
                if (c.state == ContractState.Ready && list.ButtonText("JJK_ClaimContract".Translate())) gc.Claim(c);
                if (list.ButtonText("JJK_AbandonContract".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_AbandonContractConfirm".Translate(), () => gc.Fail(c)));
                list.GapLine();
            }
            list.End(); Widgets.EndScrollView();
        }
    }
    public class Window_JujutsuDossier : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(760, 760);
        public Window_JujutsuDossier(Pawn pawn)
        { this.pawn = pawn; doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            if (pawn == null || pawn.Destroyed) { Close(); return; }
            var gc = GameComponent_JJK.Instance;
            var ac = JujutsuTraining.Class(pawn);
            var view = new Rect(0, 0, rect.width - 24, 3000);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var list = new Listing_Standard(); list.Begin(view);
            list.Label(pawn.LabelShortCap + " — " + "JJK_Dossier".Translate());
            if (list.ButtonText("JJK_RecordTitle".Translate())) Find.WindowStack.Add(new Window_CampaignHistory(pawn));
            list.Label("JJK_GradeProgress".Translate(JJK_GradeUtility.GradeLabels[(int)gc.GetGrade(pawn)], gc.GetGradeXp(pawn),
                JJK_GradeUtility.XpToNextGrade(gc.GetGradeXp(pawn)) < 0 ? "JJK_GradeCap".Translate().ToString() : JJK_GradeUtility.XpToNextGrade(gc.GetGradeXp(pawn)).ToString()));
            var ce = pawn.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            list.Label("JJK_EnergyProgress".Translate(ce?.energy.ToString("F0") ?? "0", ce?.MaxEnergy.ToString("F0") ?? "0"));
            list.Label("JJK_CastPracticeProgress".Translate(gc.CastPracticeRemaining(pawn),
                gc.CastPracticeResetTicks(pawn).ToStringTicksToPeriod()));
            list.GapLine(); list.Label("JJK_MasteryHeading".Translate());
            if (ac != null) foreach (var tree in ac.UnlockedTrees.ToList())
            {
                list.Label(tree.LabelCap + ": " + "JJK_MasteryProgress".Translate(gc.GetTechniqueXp(pawn, tree), gc.GetEffectiveMastery(pawn, tree)));
                foreach (var domain in DefDatabase<AbilityDef>.AllDefsListForReading.Where(d => d.abilityTrees?.Contains(tree) == true
                    && d.GetModExtension<DomainExtension>()?.isSimpleDomain == false))
                {
                    var ext = domain.GetModExtension<DomainExtension>();
                    int grade = domain.abilityTiers?.FirstOrDefault()?.acquireRequirement?.minLevel ?? 0;
                    list.Label("  " + domain.LabelCap + " — " + "JJK_DomainProgress".Translate(
                        JJK_GradeUtility.GradeLabels[Mathf.Clamp(grade, 0, 5)], ext.masteryToOpen, ext.minMaxCE));
                }
            }
            list.GapLine();
            if (pawn.story?.traits?.HasTrait(JJK_DefOf.JJK_SoulMender) == true) list.Label("JJK_MenderAwakened".Translate());
            else list.Label("JJK_HealingProgress".Translate(gc.GetMenderInsight(pawn), MenderInsightUtil.AwakenThreshold));
            list.Label("JJK_RCTProgress".Translate(RCTAwakeningUtil.HasRCT(pawn) ? "JJK_RCTKnown".Translate() : "JJK_RCTUnknown".Translate()));
            var insight = pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_NearDeathInsight"));
            if (insight != null) list.Label(insight.TryGetComp<HediffComp_RCTAwakening>()?.CompTipStringExtra ?? insight.LabelCap.ToString());
            if (insight?.TryGetComp<HediffComp_Disappears>() is HediffComp_Disappears expiry)
                list.Label("JJK_InsightExpires".Translate(expiry.ticksToDisappear.ToStringTicksToPeriod()));
            foreach (var vow in DefDatabase<BindingVowDef>.AllDefsListForReading.Where(v => v.grantHediffs.Any(h => pawn.health.hediffSet.HasHediff(h))))
                list.Label(vow.LabelCap + ": " + vow.description);
            if (list.ButtonText("JJK_OpenRoster".Translate())) Find.WindowStack.Add(new Window_CurseRoster(pawn));
            var vessel = HediffComp_SukunaVessel.For(pawn);
            if (vessel != null)
            {
                list.GapLine(); list.Label(vessel.CompTipStringExtra);
                list.Label("JJK_VesselOptions".Translate());
                if (vessel.CanBargain && list.ButtonText("JJK_VesselBargain".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_VesselBargainConfirm".Translate(), () => vessel.Bargain()));
            }
            if (list.ButtonText("JJK_DeepTitle".Translate())) Find.WindowStack.Add(new Window_SorceryDevelopment(pawn));
            list.GapLine(); DrawTraining(list, ac);
            list.End(); Widgets.EndScrollView();
        }
        private void DrawTraining(Listing_Standard list, AbilityClass ac)
        {
            var plan = GameComponent_Jujutsu.Instance.Plan(pawn);
            list.Label("JJK_TrainingHeading".Translate()); list.Label("JJK_TrainingDesc".Translate());
            list.CheckboxLabeled("JJK_TrainingEnabled".Translate(), ref plan.enabled);
            if (list.ButtonText("JJK_TrainingTime".Translate(plan.hour, (plan.hour + 2) % 24)))
                Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0, 24).Select(h => new FloatMenuOption(h.ToString("00") + ":00", () => plan.hour = h)).ToList()));
            if (list.ButtonText("JJK_MentorChoice".Translate(plan.mentor?.LabelShortCap ?? "JJK_NoMentor".Translate())))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("JJK_NoMentor".Translate(), () => { plan.mentor = null; plan.lesson = null; plan.lessonProgress = 0; }) };
                if (pawn.Map != null) options.AddRange(pawn.Map.mapPawns.FreeColonistsSpawned.Where(p => p != pawn && JJK_GradeUtility.IsSorcerer(p))
                    .Select(p => new FloatMenuOption(p.LabelShortCap, () => { plan.mentor = p; plan.lesson = null; plan.lessonProgress = 0; })));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (ac != null && list.ButtonText("JJK_PracticeChoice".Translate(plan.tree?.LabelCap ?? "JJK_ChooseTechnique".Translate())))
            {
                var choices = ac.UnlockedTrees.Select(t => new FloatMenuOption(t.LabelCap, () => { plan.tree = t; plan.lesson = null; plan.lessonProgress = 0; })).ToList();
                foreach (var lesson in JujutsuTraining.Lessons.Where(l => JujutsuTraining.Class(plan.mentor)?.GetLearnedAbility(l.abilityToLearn) != null
                    && ac.GetLearnedAbility(l.abilityToLearn) == null))
                    choices.Add(new FloatMenuOption("JJK_LearnFromMentor".Translate(lesson.abilityToLearn.LabelCap), () =>
                    { plan.tree = lesson.tree; plan.lesson = lesson.abilityToLearn; plan.lessonProgress = 0; }));
                if (choices.Count > 0) Find.WindowStack.Add(new FloatMenu(choices));
            }
            plan.RefreshDay(); list.Label("JJK_TrainingProgress".Translate(plan.dailyXp, plan.lessonProgress));
            if (JujutsuTraining.Valid(plan, false) && list.ButtonText("JJK_PracticeNow".Translate()))
            {
                Thing post = pawn.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_TrainingPost"))
                    .FirstOrDefault(t => pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some));
                if (post != null) pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_Practice"), post, plan.mentor));
                else Messages.Message("JJK_NoTrainingPost".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
    }
    public class Window_CurseRoster : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(820, 650);
        public Window_CurseRoster(Pawn pawn) { this.pawn = pawn; doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            if (pawn == null || pawn.Destroyed) { Close(); return; }
            var data = GameComponent_JJK.Instance.GetAbsorbedCurses(pawn);
            var view = new Rect(0, 0, rect.width - 24, 300 + data.Count * 230);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_RosterCount".Translate(data.Count, AbsorbedCurseData.MaxStored));
            list.Label("JJK_RosterDesc".Translate());
            list.Label("JJK_UzumakiSelection".Translate(data.SelectedCount,
                data.SelectedCount == 0 ? 0 : Mathf.RoundToInt((25f + data.SelectedPower * 0.35f) * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput)))));
            if (list.ButtonText("JJK_SelectAllCurses".Translate())) data.selectiveSacrifice = false;
            foreach (Pawn curse in data.Pawns)
            {
                list.GapLine(); list.Label(curse.LabelCap + " — " + curse.kindDef.LabelCap + " — " + curse.health.summaryHealth.SummaryHealthPercent.ToStringPercent());
                string wounds = string.Join(", ", curse.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => h.LabelCap.ToString()));
                if (wounds.Length > 0) list.Label(wounds);
                if (list.ButtonText("JJK_InspectCurse".Translate())) Find.WindowStack.Add(new Dialog_InfoCard(curse));
                var boss = curse.TryGetComp<CompBossCurse>();
                list.Label(boss == null ? "JJK_CurseMelee".Translate().ToString() : "JJK_CurseAbilities".Translate(
                    string.Join(", ", boss.Props.attacks?.Select(a => a.projectile.LabelCap.ToString()) ?? new string[0]), boss.Props.domain?.LabelCap ?? "—").ToString());
                bool selected = data.Included(curse), before = selected;
                list.CheckboxLabeled("JJK_SacrificeThisCurse".Translate(), ref selected);
                if (before != selected) data.SelectSacrifice(curse, selected);
                if (list.ButtonText((data.releaseId == curse.ThingID ? "✓ " : "") + "JJK_ReleaseThisNext".Translate())) data.releaseId = curse.ThingID;
            }
            list.End(); Widgets.EndScrollView();
        }
    }
}
