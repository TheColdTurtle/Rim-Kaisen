using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace JJKMod
{
    public class Window_Kogane : Window
    {
        private Vector2 scroll;
        private float contentHeight = 2000;
        private static bool Button(Listing_Standard list, string label) => Widgets.ButtonText(list.GetRect(30f), label);
        public override Vector2 InitialSize => new Vector2(790, 700);
        public Window_Kogane() { doCloseX = true; absorbInputAroundWindow = true; forcePause = true; }
        public override void DoWindowContents(Rect inRect)
        {
            var gc = GameComponent_CullingGame.Instance; if (gc == null) return;
            var d = gc.data;
            var view = new Rect(0, 0, inRect.width - 24, Mathf.Max(inRect.height, contentHeight));
            Widgets.BeginScrollView(inRect, ref scroll, view);
            var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_Kogane".Translate()); list.Label("JJK_CullingRules".Translate()); list.GapLine();
            list.Label(("JJK_CullingPhase" + d.phase).Translate());
            if (d.phase == CullingPhase.Dormant || d.phase == CullingPhase.Offered)
            {
                Map home = Find.CurrentMap;
                list.Label("JJK_CullingEligibility".Translate());
                if (gc.Eligible(home) && Button(list, "JJK_CullingBegin".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_CullingConfirm".Translate(), () => gc.Begin(home)));
            }
            if (gc.Active || d.phase == CullingPhase.Victory)
            {
                list.Label("JJK_CullingCampaign".Translate(gc.Anchors, d.raids, d.breaches));
                foreach (var site in d.sites.Where(s => s != null && !s.Destroyed))
                { if (Button(list, site.LabelCap + " — " + site.Progress)) CameraJumper.TryJumpAndSelect(site); }
                list.GapLine();
                foreach (CullingRule rule in System.Enum.GetValues(typeof(CullingRule)))
                {
                    bool unlocked = gc.RuleEnabled(rule);
                    list.Label(("JJK_CullingRule" + rule).Translate() + " — " + (unlocked ? "JJK_CullingEnabled".Translate() : "JJK_CullingRuleCost".Translate()));
                    if ((int)rule >= 2) list.Label(("JJK_CullingRuleDesc" + rule).Translate());
                    if (gc.Active && !unlocked && Button(list, "JJK_CullingChoosePayer".Translate(("JJK_CullingRule" + rule).Translate())))
                    {
                        var choices = d.players.Where(p => GameComponent_CullingGame.LivingPlayer(p.pawn) && p.points >= 100)
                            .Select(p => new FloatMenuOption(p.name + " (" + p.points + ")", () => gc.BuyRule(p.pawn, rule))).ToList();
                        if (choices.Count == 0) choices.Add(new FloatMenuOption("JJK_CullingNoPayer".Translate(), null));
                        Find.WindowStack.Add(new Window_CullingChoice(choices));
                    }
                }
                list.GapLine(); list.Label("JJK_CullingRoster".Translate());
                list.Label("JJK_RivalPressure".Translate(d.rivalPressure));
                foreach (var p in d.players.Where(p => GameComponent_CullingGame.LivingPlayer(p.pawn)))
                {
                    list.Label("JJK_CullingPlayer".Translate(p.name, p.points,
                        Mathf.Max(0, p.lastScoreTick + GameComponent_CullingGame.InactivityTicks - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
                    if (gc.Active && d.negotiatedTruce && p.points >= 20 && p.pawn.Map?.Parent is CullingSite local
                        && !local.finale && !local.truceUsed && local.Remaining == 0 && local.wave < local.WaveLimit
                        && Button(list, "JJK_NegotiateTruce".Translate())) gc.Negotiate(p.pawn, local);
                    if (gc.Active && d.bounties && p.points >= 20 && Button(list, "JJK_PostBounty".Translate()))
                    {
                        var choices = d.players.Where(r => r.pawn != null && !r.pawn.Dead && !r.pawn.Destroyed
                            && r.bounty == 0 && r.pawn.HostileTo(Faction.OfPlayer))
                            .Select(r => new FloatMenuOption(r.name, () => gc.PostBounty(p.pawn, r.pawn))).ToList();
                        if (choices.Count > 0) Find.WindowStack.Add(new Window_CullingChoice(choices));
                    }
                    if (gc.Active && d.pointTransfer && p.points > 0 && Button(list, "JJK_CullingTransferOne".Translate()))
                    {
                        var choices = d.players.Where(to => to != p && GameComponent_CullingGame.LivingPlayer(to.pawn))
                            .Select(to => new FloatMenuOption(to.name, () => gc.Transfer(p.pawn, to.pawn, 1))).ToList();
                        if (choices.Count > 0) Find.WindowStack.Add(new Window_CullingChoice(choices));
                    }
                    if (gc.Active && d.pointTransfer && p.points > 0 && Button(list, "JJK_CullingTransferAll".Translate()))
                    {
                        var choices = d.players.Where(to => to != p && GameComponent_CullingGame.LivingPlayer(to.pawn))
                            .Select(to => new FloatMenuOption(to.name, () => gc.Transfer(p.pawn, to.pawn, p.points))).ToList();
                        if (choices.Count > 0) Find.WindowStack.Add(new Window_CullingChoice(choices));
                    }
                    if (d.phase == CullingPhase.Victory && !d.rewardClaimed && p.pawn.Map == d.home
                        && GameComponent_JJK.Instance.GetGrade(p.pawn) == SorcererGrade.Special
                        && Button(list, "JJK_CullingClaim".Translate(p.name)))
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_CullingClaimConfirm".Translate(p.name), () => gc.Claim(p.pawn)));
                }
                foreach (var p in d.players.Where(p => p.pawn != null && !p.pawn.Dead && !p.pawn.Destroyed && p.pawn.Faction != Faction.OfPlayer))
                    list.Label("JJK_CullingRival".Translate(p.name, p.points, p.pawn.MapHeld?.Parent.LabelCap ?? "?")
                        + (p.bounty > 0 ? " — " + "JJK_BountyValue".Translate(p.bounty + 10).ToString() : ""));
                if (gc.Active && Button(list, "JJK_CullingAbandon".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("JJK_CullingAbandonConfirm".Translate(), gc.Abandon));
            }
            contentHeight = list.CurHeight + 24; list.End(); Widgets.EndScrollView();
        }
    }
    // A regular dialog keeps the picker usable without a mouse-hover-dependent FloatMenu.
    public class Window_CullingChoice : Window
    {
        private readonly List<FloatMenuOption> choices;
        private Vector2 scroll;
        public Window_CullingChoice(List<FloatMenuOption> choices)
        { this.choices = choices; doCloseX = true; absorbInputAroundWindow = true; forcePause = true; }
        public override Vector2 InitialSize => new Vector2(480, Mathf.Min(600, 80 + choices.Count * 36));
        public override void DoWindowContents(Rect rect)
        {
            var view = new Rect(0, 0, rect.width - 20, choices.Count * 36);
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0;
            foreach (var choice in choices)
            {
                var row = new Rect(0, y, view.width, 32); y += 36;
                if (choice.action == null) Widgets.Label(row, choice.Label);
                else if (Widgets.ButtonText(row, choice.Label)) { choice.action(); Close(); }
            }
            Widgets.EndScrollView();
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_KoganeGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var g in values) yield return g;
            if (!GameComponent_CullingGame.LivingPlayer(__instance) || GameComponent_CullingGame.Instance?.Player(__instance) == null) yield break;
            yield return new Command_Action { defaultLabel = "JJK_Kogane".Translate(), defaultDesc = "JJK_CullingOpenDesc".Translate(),
                icon = TexCommand.ClearPrioritizedWork, action = () => Find.WindowStack.Add(new Window_Kogane()) };
        }
    }
}
