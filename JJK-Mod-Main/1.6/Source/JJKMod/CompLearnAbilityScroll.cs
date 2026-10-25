using System.Collections.Generic;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // A readable scroll that teaches one cursed-technique ability. Reading it
    // unlocks the ability's tree (if needed) and learns the ability on the
    // reader's cursed-technique class. This is how Simple Domain - and the other
    // anti-domain techniques to come - are learned: never automatically, only by
    // a sorcerer studying a scroll or similar item. Generic and data-driven, so
    // every future technique scroll reuses this one comp.
    public class CompProperties_LearnAbilityScroll : CompProperties
    {
        // The discipline the ability belongs to (JJK_CursedTechniques). The reader
        // must already have this unlocked - i.e. be a sorcerer with a reserve.
        public AbilityClassDef abilityClass;
        // The tree to unlock on read (a prerequisite of abilityToLearn).
        public AbilityTreeDef tree;
        // The ability the scroll teaches.
        public AbilityDef abilityToLearn;
        public int readDurationTicks = 300;
        public bool destroyOnRead = true;

        public CompProperties_LearnAbilityScroll()
        {
            compClass = typeof(CompLearnAbilityScroll);
        }
    }

    public class CompLearnAbilityScroll : ThingComp
    {
        public CompProperties_LearnAbilityScroll Props => (CompProperties_LearnAbilityScroll)props;

        private AbilityClass ClassFor(Pawn p) =>
            p?.GetComp<CompAbilities>()?.GetAbilityClass(Props.abilityClass);

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            AbilityClass ac = ClassFor(selPawn);
            if (ac == null || !ac.Unlocked)
            {
                yield return new FloatMenuOption(
                    "JJK_CannotReadScroll".Translate("JJK_CannotReadNotSorcerer".Translate()), null);
                yield break;
            }
            if (Props.abilityToLearn != null && ac.GetLearnedAbility(Props.abilityToLearn) != null)
            {
                yield return new FloatMenuOption(
                    "JJK_CannotReadScroll".Translate("JJK_CannotReadAlreadyLearned".Translate()), null);
                yield break;
            }
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                yield return new FloatMenuOption(
                    "JJK_CannotReadScroll".Translate("NoPath".Translate()), null);
                yield break;
            }

            yield return new FloatMenuOption("JJK_ReadScroll".Translate(parent.LabelShort), delegate
            {
                Job job = JobMaker.MakeJob(JJK_DefOf.JJK_ReadScroll, parent);
                job.count = 1;
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }

        // Called by JobDriver_ReadScroll once the reader finishes studying.
        public void DoEffect(Pawn reader)
        {
            AbilityClass ac = ClassFor(reader);
            if (ac == null || !ac.Unlocked) return;

            if (Props.tree != null && !ac.UnlockedTrees.Contains(Props.tree))
                ac.UnlockTree(Props.tree);
            if (Props.abilityToLearn != null && ac.GetLearnedAbility(Props.abilityToLearn) == null)
                ac.LearnAbility(Props.abilityToLearn, false);

            if (reader.Faction != null && reader.Faction.IsPlayer)
            {
                Messages.Message(
                    "JJK_ScrollLearned".Translate(reader.LabelShortCap,
                        Props.abilityToLearn?.label ?? parent.LabelShort),
                    reader, MessageTypeDefOf.PositiveEvent, false);
            }

            if (Props.destroyOnRead && !parent.Destroyed) parent.Destroy();
        }
    }
}
