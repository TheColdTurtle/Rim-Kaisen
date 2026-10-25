using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public static class PromotionTrials
    {
        public static int Tier(Pawn p) => Mathf.Clamp((int)GameComponent_JJK.Instance.GetGrade(p), 1, 3);
        public static int Xp(int grade) => new[] { 50, 120, 240, 450 }[Mathf.Clamp(grade, 0, 3)];
        public static float Points(int grade) => new[] { 180f, 360f, 650f, 1000f }[Mathf.Clamp(grade, 0, 3)];
        public static ThingDef Reward(int grade) => DefDatabase<ThingDef>.GetNamed(grade < 2 ? "JJK_KusakabeKatana" : "JJK_NanamiBluntSword");
        public static string Reason(Faction school, Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Faction != Faction.OfPlayer
                || !p.IsFreeColonist || !JJK_GradeUtility.IsSorcerer(p) || !p.Map.IsPlayerHome) return "JJK_TrialCandidateInvalid".Translate();
            int grade = (int)GameComponent_JJK.Instance.GetGrade(p);
            if (grade >= 4) return "JJK_TrialGradeCap".Translate();
            var record = GameComponent_SorcererStudy.Instance.For(p);
            if ((record.trialPasses & (1 << grade)) != 0) return "JJK_TrialAlreadyPassed".Translate();
            if (record.nextTrial > Find.TickManager.TicksGame) return "JJK_TrialCooldown".Translate((record.nextTrial - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            if (GameComponent_Jujutsu.Instance.contracts.Any(c => c.promotionTrial && c.trialCandidate == p
                && (c.state == ContractState.Active || c.state == ContractState.Ready))) return "JJK_TrialActive".Translate();
            return GameComponent_Jujutsu.Instance.CanAccept(school, p.Map, Tier(p), ContractKind.Infestation) ? null : "JJK_TrialSchoolUnavailable".Translate();
        }
        public static bool Valid(JujutsuContract c) => c.trialCandidate != null && !c.trialCandidate.Dead && !c.trialCandidate.Destroyed
            && !c.trialCandidate.Downed && c.trialCandidate.Spawned && c.trialCandidate.Map == c.map
            && c.trialCandidate.Faction == Faction.OfPlayer && JJK_GradeUtility.IsSorcerer(c.trialCandidate)
            && (c.Remaining > 0 || c.trialEngaged.Count >= c.trialRequired);
        public static void RecordHit(Pawn victim, Pawn actor, float damage)
        {
            if (victim == null || actor == null || damage <= 0 || victim.Faction == Faction.OfPlayer) return;
            actor = actor.health?.hediffSet?.hediffs.OfType<Hediff_ShikigamiBond>().FirstOrDefault()?.owner ?? actor;
            foreach (var c in GameComponent_Jujutsu.Instance.contracts.Where(c => c.promotionTrial && c.state == ContractState.Active
                && c.trialCandidate == actor && c.threats.Contains(victim)))
            {
                string id = victim.GetUniqueLoadID();
                if (!c.trialEngaged.Contains(id)) c.trialEngaged.Add(id);
            }
        }
        public static void Complete(JujutsuContract c)
        {
            var r = GameComponent_SorcererStudy.Instance.For(c.trialCandidate);
            r.trialPasses |= 1 << c.trialGrade; r.nextTrial = Find.TickManager.TicksGame + 300000;
            GameComponent_JJK.Instance.AddGradeXp(c.trialCandidate, Xp(c.trialGrade));
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PromotionParticipation
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        { if (Current.Game != null) PromotionTrials.RecordHit(__instance, dinfo.Instigator as Pawn, totalDamageDealt); }
    }
    public class Window_PromotionTrials : Window
    {
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(780, 680);
        public Window_PromotionTrials() { doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            var pawns = Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Where(JJK_GradeUtility.IsSorcerer).ToList();
            if (pawns == null) return;
            var schools = GameComponent_Jujutsu.Schools.ToList();
            var view = new Rect(0, 0, rect.width - 24, 160 + pawns.Count * schools.Count * 205);
            Widgets.BeginScrollView(rect, ref scroll, view); var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_TrialDescription".Translate()); list.GapLine();
            foreach (var p in pawns) foreach (var school in schools)
            {
                list.Label(p.LabelShortCap + " — " + school.Name);
                string reason = PromotionTrials.Reason(school, p);
                if (reason != null) { list.Label(reason); list.GapLine(); continue; }
                int grade = (int)GameComponent_JJK.Instance.GetGrade(p);
                list.Label("JJK_TrialReward".Translate(PromotionTrials.Xp(grade), PromotionTrials.Reward(grade).LabelCap, 10 * PromotionTrials.Tier(p)));
                if (list.ButtonText("JJK_TrialBegin".Translate()))
                {
                    var candidate = p; var giver = school;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_TrialConfirm".Translate(candidate.LabelShortCap),
                        () => GameComponent_Jujutsu.Instance.Accept(giver, candidate.Map, PromotionTrials.Tier(candidate), ContractKind.Infestation, candidate)));
                }
                list.GapLine();
            }
            list.End(); Widgets.EndScrollView();
        }
    }
}
