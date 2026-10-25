using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class CompProperties_BindingVowStation : CompProperties
    {
        public int ritualDurationTicks = 600;

        public CompProperties_BindingVowStation()
        {
            compClass = typeof(CompBindingVowStation);
        }
    }

    // The binding-vow altar. A sorcerer who is not already bound can right-click it
    // to swear a binding vow; they walk over, perform the ritual, and the vow's
    // hediff is applied. Which vow was chosen is held here per-pawn until the
    // ritual completes (transient - a save mid-walk simply cancels the choice).
    public class CompBindingVowStation : ThingComp
    {
        public CompProperties_BindingVowStation Props => (CompProperties_BindingVowStation)props;

        private readonly Dictionary<Pawn, BindingVowDef> pending = new Dictionary<Pawn, BindingVowDef>();

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!JJK_GradeUtility.IsSorcerer(selPawn))
            {
                yield return new FloatMenuOption(
                    "JJK_VowCannot".Translate("JJK_VowNotSorcerer".Translate()), null);
                yield break;
            }
            if (BindingVowUtil.HasAnyVow(selPawn))
            {
                yield return new FloatMenuOption(
                    "JJK_VowCannot".Translate("JJK_VowAlreadyBound".Translate()), null);
                yield break;
            }
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    "JJK_VowCannot".Translate("NoPath".Translate()), null);
                yield break;
            }

            foreach (BindingVowDef vow in DefDatabase<BindingVowDef>.AllDefsListForReading)
            {
                BindingVowDef captured = vow;
                yield return new FloatMenuOption("JJK_VowSwear".Translate(captured.LabelCap), delegate
                {
                    System.Action<Pawn> order = protectedPawn => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(captured.description, () =>
                    {
                        OrderVow(selPawn, captured, protectedPawn);
                    }));
                    if (!captured.requiresProtectedPawn) order(null);
                    else
                    {
                        var options = new List<FloatMenuOption>();
                        foreach (var ally in parent.Map.mapPawns.FreeColonistsSpawned)
                            if (ally != selPawn && !ally.Dead)
                            { var target = ally; options.Add(new FloatMenuOption(target.LabelShortCap, () => order(target))); }
                        if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
                    }
                });
            }
        }

        // Hand back (and clear) the vow this pawn chose at the menu.
        public void OrderVow(Pawn pawn, BindingVowDef vow, Pawn protectedPawn = null)
        {
            if (!SorceryDevelopment.CanAct(pawn) || BindingVowUtil.HasAnyVow(pawn) || !JJK_GradeUtility.IsSorcerer(pawn)) return;
            pending[pawn] = vow;
            Job job = JobMaker.MakeJob(JJK_DefOf.JJK_SwearBindingVow, parent);
            if (protectedPawn != null) job.targetB = protectedPawn;
            pawn.jobs.TryTakeOrderedJob(job);
        }
        public BindingVowDef ConsumePending(Pawn pawn)
        {
            if (pawn != null && pending.TryGetValue(pawn, out BindingVowDef vow))
            {
                pending.Remove(pawn);
                return vow;
            }
            return null;
        }
    }
}
