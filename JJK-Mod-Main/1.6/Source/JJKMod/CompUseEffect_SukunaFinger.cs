using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Consuming one of Sukuna's fingers: a special-grade cursed object that
    // grants a surge of power (grade XP + the growing Sukuna's Influence hediff)
    // at a price - the King of Curses' malice may briefly seize the eater.
    // Sorcerers only; twenty fingers exist in lore, so each is a rare find.
    public class CompProperties_SukunaFinger : CompProperties_UseEffect
    {
        public int gradeXp = 400;
        public float influencePerFinger = 0.05f;
        public float berserkChance = 0.25f;

        public CompProperties_SukunaFinger()
        {
            compClass = typeof(CompUseEffect_SukunaFinger);
        }
    }

    public class CompUseEffect_SukunaFinger : CompUseEffect
    {
        public new CompProperties_SukunaFinger Props => (CompProperties_SukunaFinger)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (usedBy == null || gc == null) return;

            gc.AddGradeXp(usedBy, Props.gradeXp);

            // Sukuna's influence deepens with each finger.
            if (JJK_DefOf.JJK_SukunaInfluence != null && usedBy.health != null)
            {
                Hediff influence = usedBy.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_SukunaInfluence);
                if (influence == null)
                {
                    influence = usedBy.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence);
                    influence.Severity = Props.influencePerFinger;
                }
                else
                {
                    influence.Severity = Mathf.Min(1f, influence.Severity + Props.influencePerFinger);
                }
            }

            Messages.Message("JJK_FingerConsumed".Translate(usedBy.LabelShortCap),
                usedBy, MessageTypeDefOf.NeutralEvent, false);

            // The King of Curses stirs.
            if (Rand.Chance(Props.berserkChance)
                && usedBy.mindState?.mentalStateHandler != null && !usedBy.InMentalState)
            {
                usedBy.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.Berserk, "JJK_SukunaStirs".Translate(), forced: true);
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (!JJK_GradeUtility.IsSorcerer(p))
                return "JJK_CannotReadNotSorcerer".Translate();
            return base.CanBeUsedBy(p);
        }
    }
}
