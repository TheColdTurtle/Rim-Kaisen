using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;

namespace JJKMod
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_SorceryDevelopmentGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            var p = __instance;
            if (p.Faction != Faction.OfPlayer || !JJK_GradeUtility.IsSorcerer(p)) yield break;
            yield return new Command_Action { defaultLabel = "JJK_DeepTitle".Translate(), defaultDesc = "JJK_DeepIntro".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/Xenotypes/EliteSorcerer"), action = () => Find.WindowStack.Add(new Window_SorceryDevelopment(p)) };
            if (!SorceryDevelopment.CanAct(p)) yield break;
            if (JJK_Utils.HasCursedTechnique(p))
            {
                var reveal = new Command_Target { defaultLabel = "JJK_DeepReveal".Translate(), defaultDesc = "JJK_DeepRevealDesc".Translate(),
                    icon = TexCommand.Attack, targetingParams = TargetingParameters.ForAttackAny(),
                    action = t => { if (!SorceryDevelopment.Reveal(p, t.Pawn)) Messages.Message("JJK_DeepRevealInvalid".Translate(), p, MessageTypeDefOf.RejectInput); } };
                if (!SorceryDevelopment.CanSpeak(p) || SorceryDevelopment.State(p).revealCooldown > SorceryDevelopment.Now)
                    reveal.Disable("JJK_DeepRevealUnavailable".Translate(Window_SorceryDevelopment.Seconds(SorceryDevelopment.State(p).revealCooldown - SorceryDevelopment.Now)));
                yield return reveal;
            }
            if (MapComponent_ResidualEnergy.Skilled(p))
            {
                var scan = new Command_Action { defaultLabel = "JJK_DeepScan".Translate(), defaultDesc = "JJK_DeepScanDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/Genes/SixEyes"), action = () => p.Map.GetComponent<MapComponent_ResidualEnergy>().Scan(p) };
                if (SorceryDevelopment.State(p).nextTrack > SorceryDevelopment.Now) scan.Disable("JJK_DeepCooling".Translate());
                yield return scan;
            }
        }
    }
    public class Window_SorceryDevelopment : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;
        private float height = 2400;
        public override Vector2 InitialSize => new Vector2(780, 720);
        public Window_SorceryDevelopment(Pawn pawn) { this.pawn = pawn; doCloseX = true; absorbInputAroundWindow = true; }
        public static string Seconds(int ticks) => "JJK_DeepSeconds".Translate((Math.Max(0, ticks) / 60f).ToString("F1"));
        private static void Confirm(string text, Action action) => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, action));
        public override void DoWindowContents(Rect rect)
        {
            if (pawn == null || pawn.Destroyed) return;
            var r = SorceryDevelopment.State(pawn);
            var view = new Rect(0, 0, rect.width - 20, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            // Listing_Standard starts another column when it outgrows its rect.
            // Measure with ample vertical space, then size the scroll view next frame.
            var l = new Listing_Standard(); l.Begin(new Rect(0, 0, view.width, 100000));
            l.Label(pawn.LabelShortCap + " — " + "JJK_DeepTitle".Translate()); l.Label("JJK_DeepIntro".Translate());
            l.GapLine(); l.Label("JJK_DeepVows".Translate());
            foreach (var v in DefDatabase<BindingVowDef>.AllDefsListForReading.Where(v => v.grantHediffs.Any(h => pawn.health.hediffSet.HasHediff(h))))
                l.Label(v.LabelCap + ": " + v.description);
            if (SorceryDevelopment.Has(pawn, "JJK_Vow_Protector"))
                l.Label("JJK_DeepProtectedStatus".Translate(r.protectedPawn?.LabelShortCap ?? "JJK_DeepMissing".Translate(),
                    (r.protectionBroken ? "JJK_DeepBroken" : r.ProtectionActive ? "JJK_DeepActive" : "JJK_DeepDormant").Translate()));
            if (r.revealUntil > SorceryDevelopment.Now) l.Label("JJK_DeepRevealStatus".Translate(r.revealedTo?.LabelShortCap,
                Seconds(r.revealUntil - SorceryDevelopment.Now)));
            l.GapLine(); l.Label("JJK_DeepDomains".Translate()); l.Label("JJK_DeepDomainRules".Translate());
            var ac = pawn.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            if (ac != null) foreach (var tree in ac.UnlockedTrees.Where(t => DefDatabase<TaranMagicFramework.AbilityDef>.AllDefsListForReading
                .Any(a => a.abilityTrees?.Contains(t) == true && a.GetModExtension<DomainExtension>()?.isSimpleDomain == false)))
            {
                var captured = tree; var refinement = r.Refinement(tree); int xp = GameComponent_JJK.Instance.GetTechniqueXp(pawn, tree);
                l.Label(tree.LabelCap + ": " + xp + "/1000 — " + ("JJK_DeepDomain" + refinement).Translate());
                if (refinement != DomainRefinement.None) l.Label(("JJK_DeepDomain" + refinement + "Desc").Translate());
                if (refinement == DomainRefinement.None && xp >= 1000)
                    foreach (DomainRefinement choice in new[] { DomainRefinement.Condensed, DomainRefinement.Fortified, DomainRefinement.Economical })
                    {
                        var chosen = choice;
                        if (choice == DomainRefinement.Fortified && !DefDatabase<TaranMagicFramework.AbilityDef>.AllDefsListForReading.Any(a => a.abilityTrees?.Contains(tree) == true && a.GetModExtension<DomainExtension>()?.closed == true)) continue;
                        string description = ("JJK_DeepDomain" + choice + "Desc").Translate(); l.Label(description);
                        if (l.ButtonText(("JJK_DeepDomain" + choice).Translate()))
                            Confirm(description + "\n" + "JJK_DeepPermanent".Translate(), () => r.Refine(captured, chosen));
                    }
            }
            l.GapLine(); l.Label("JJK_DeepTools".Translate()); l.Label("JJK_DeepToolRules".Translate());
            var tool = pawn.equipment?.Primary?.TryGetComp<Comp_CursedTool>();
            if (tool != null) l.Label(tool.CompInspectStringExtra());
            l.GapLine(); l.Label("JJK_DeepInheritance".Translate()); l.Label("JJK_DeepInheritanceRules".Translate());
            for (int i = 0; i < r.donors.Count && i < r.heirs.Count; i++)
                l.Label(r.donors[i]?.LabelCap.ToString() + " → " + r.heirs[i]?.LabelCap.ToString());
            for (int i = 0; i < ShadowInheritance.Pairs.GetLength(0); i++)
            {
                int pair = i;
                string label = ShadowInheritance.Shadow(ShadowInheritance.Pairs[i, 0]).LabelCap + " → "
                    + ShadowInheritance.Shadow(ShadowInheritance.Pairs[i, 1]).LabelCap + ": "
                    + DefDatabase<HediffDef>.GetNamed(ShadowInheritance.Pairs[i, 2]).description;
                l.Label(label);
                if (ShadowInheritance.CanInherit(pawn, pair) && l.ButtonText("JJK_DeepInherit".Translate()))
                    Confirm(label + "\n" + "JJK_DeepPermanent".Translate(), () => ShadowInheritance.Inherit(pawn, pair));
            }
            var vessel = HediffComp_SukunaVessel.For(pawn);
            if (vessel != null)
            {
                l.GapLine(); l.Label("JJK_DeepVessel".Translate()); l.Label(vessel.CompTipStringExtra);
                l.Label("JJK_DeepVesselProgress".Translate(r.resolve, r.rescues));
                l.Label("JJK_DeepConfrontDesc".Translate());
                if (VesselDevelopment.CanConfront(pawn) && l.ButtonText("JJK_DeepConfront".Translate())) VesselDevelopment.Confront(pawn);
                l.Label("JJK_DeepQuietDesc".Translate());
                if (vessel.CanBargain && !SorceryDevelopment.Has(pawn, "JJK_VesselQuietPrice") && l.ButtonText("JJK_DeepQuiet".Translate()))
                    Confirm("JJK_DeepQuietDesc".Translate(), () => VesselDevelopment.QuietBargain(pawn));
                l.Label("JJK_VesselBargainConfirm".Translate());
                if (vessel.CanBargain && l.ButtonText("JJK_VesselBargain".Translate())) Confirm("JJK_VesselBargainConfirm".Translate(), () => vessel.Bargain());
                if (r.rescueDeadline > SorceryDevelopment.Now) l.Label("JJK_DeepRescueRemaining".Translate(Seconds(r.rescueDeadline - SorceryDevelopment.Now)));
            }
            height = l.CurHeight + 25; l.End(); Widgets.EndScrollView();
        }
    }
}
