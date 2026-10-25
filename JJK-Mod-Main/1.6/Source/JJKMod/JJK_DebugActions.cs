using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugActions
    {
        // Click any sorcerer (colonist OR enemy) to force them to open their innate
        // Domain Expansion right now - the only way to make a non-player sorcerer
        // manifest a domain on demand (e.g. to test a domain clash). Opens the
        // domain matching a cursed technique they have unlocked.
        [DebugAction("Rim-Kaisen", "Force open Domain Expansion", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceOpenDomain()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            AbilityClass ac = pawn.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            if (ac == null || ac.UnlockedTrees == null)
            {
                Messages.Message($"{pawn.LabelShort} is not a sorcerer.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            AbilityDef domainDef = DefDatabase<AbilityDef>.AllDefsListForReading.FirstOrDefault(def =>
            {
                DomainExtension ext = def.GetModExtension<DomainExtension>();
                if (ext == null || ext.isSimpleDomain) return false;
                AbilityTreeDef tree = def.abilityTrees?.FirstOrDefault();
                return tree != null && ac.UnlockedTrees.Contains(tree);
            });

            if (domainDef == null)
            {
                Messages.Message($"{pawn.LabelShort} has no cursed technique with a domain.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Refill so the domain does not immediately collapse from upkeep.
            TryRefill(pawn);
            pawn.Map.GetComponent<MapComponent_DomainManager>()?.Open(pawn, domainDef);
            Messages.Message($"{pawn.LabelShort} opened {domainDef.label}.", pawn, MessageTypeDefOf.NeutralEvent, false);
        }

        // Click an RCT sorcerer to awaken them as a soul mender immediately,
        // instead of grinding out the tend-the-wounded insight.
        [DebugAction("Rim-Kaisen", "Awaken soul mender", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AwakenSoulMender()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn?.story?.traits == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (JJK_DefOf.JJK_SoulMender != null && pawn.story.traits.HasTrait(JJK_DefOf.JJK_SoulMender))
            {
                Messages.Message($"{pawn.LabelShort} is already a soul mender.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (JJK_DefOf.JJK_RCT == null || !pawn.story.traits.HasTrait(JJK_DefOf.JJK_RCT))
            {
                Messages.Message($"{pawn.LabelShort} does not have the reverse cursed technique trait.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            MenderInsightUtil.Awaken(pawn);
            Messages.Message($"{pawn.LabelShort} awakened as a soul mender.",
                pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        // Click a pawn to refill its cursed energy to maximum.
        [DebugAction("Rim-Kaisen", "Refill cursed energy", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RefillCursedEnergy()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (TryRefill(pawn))
                Messages.Message($"Refilled cursed energy for {pawn.LabelShort}.", pawn, MessageTypeDefOf.PositiveEvent, false);
            else
                Messages.Message($"{pawn.LabelShort} has no cursed energy reserve.", MessageTypeDefOf.RejectInput, false);
        }

        // Refill cursed energy for every colonist on the current map at once.
        [DebugAction("Rim-Kaisen", "Refill cursed energy (all colonists)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RefillCursedEnergyAllColonists()
        {
            int count = 0;
            foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
            {
                if (TryRefill(p)) count++;
            }
            Messages.Message($"Refilled cursed energy for {count} colonist(s).", MessageTypeDefOf.PositiveEvent, false);
        }

        private static bool TryRefill(Pawn pawn)
        {
            CompAbilities comp = pawn?.GetComp<CompAbilities>();
            if (comp == null) return false;
            AbilityResource res = comp.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res == null) return false;
            res.energy = res.MaxEnergy;
            return true;
        }

        // Click a pawn to strip any binding vows (for testing - vows are permanent).
        [DebugAction("Rim-Kaisen", "Clear binding vows", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearBindingVows()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn?.health == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            int removed = 0;
            foreach (BindingVowDef vow in DefDatabase<BindingVowDef>.AllDefsListForReading)
            {
                foreach (HediffDef hd in vow.grantHediffs)
                {
                    Hediff h;
                    while ((h = pawn.health.hediffSet.GetFirstHediffOfDef(hd)) != null)
                    {
                        pawn.health.RemoveHediff(h);
                        removed++;
                    }
                }
            }
            Messages.Message($"Removed {removed} binding-vow hediff(s) from {pawn.LabelShort}.",
                pawn, MessageTypeDefOf.NeutralEvent, false);
        }

        // Click a sorcerer to promote them one grade (Grade 4 -> ... -> Calamity).
        [DebugAction("Rim-Kaisen", "Increase sorcerer grade (+1)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void IncreaseGrade()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var comp = GameComponent_JJK.Instance;
            if (comp == null) return;
            if (comp.IncreaseGrade(pawn))
                Messages.Message($"{pawn.LabelShort} promoted to {JJK_GradeUtility.GradeLabels[(int)comp.GetGrade(pawn)]}.",
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            else if (!JJK_GradeUtility.IsSorcerer(pawn))
                Messages.Message($"{pawn.LabelShort} has no cursed energy reserve.", MessageTypeDefOf.RejectInput, false);
            else
                Messages.Message($"{pawn.LabelShort} is already at the maximum grade.", MessageTypeDefOf.RejectInput, false);
        }

        // Click a sorcerer to demote them one grade.
        [DebugAction("Rim-Kaisen", "Decrease sorcerer grade (-1)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DecreaseGrade()
        {
            Pawn pawn = UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (pawn == null)
            {
                Messages.Message("No pawn under cursor.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var comp = GameComponent_JJK.Instance;
            if (comp == null) return;
            if (comp.DecreaseGrade(pawn))
                Messages.Message($"{pawn.LabelShort} demoted to {JJK_GradeUtility.GradeLabels[(int)comp.GetGrade(pawn)]}.",
                    pawn, MessageTypeDefOf.NeutralEvent, false);
            else if (!JJK_GradeUtility.IsSorcerer(pawn))
                Messages.Message($"{pawn.LabelShort} has no cursed energy reserve.", MessageTypeDefOf.RejectInput, false);
            else
                Messages.Message($"{pawn.LabelShort} is already at the lowest grade.", MessageTypeDefOf.RejectInput, false);
        }
    }
}
