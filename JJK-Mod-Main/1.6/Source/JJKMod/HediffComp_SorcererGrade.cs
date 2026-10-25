using Verse;

namespace JJKMod
{
    public class HediffCompProperties_SorcererGrade : HediffCompProperties
    {
        public HediffCompProperties_SorcererGrade()
        {
            compClass = typeof(HediffComp_SorcererGrade);
        }
    }

    // Adds grade-XP progress to the JJK_SorcererGrade hediff tooltip.
    public class HediffComp_SorcererGrade : HediffComp
    {
        public HediffCompProperties_SorcererGrade Props => (HediffCompProperties_SorcererGrade)props;

        public override string CompTipStringExtra
        {
            get
            {
                var comp = GameComponent_JJK.Instance;
                if (comp == null) return null;
                Pawn pawn = parent.pawn;
                int xp = comp.GetGradeXp(pawn);
                int toNext = JJK_GradeUtility.XpToNextGrade(xp);
                if (comp.GetGrade(pawn) >= SorcererGrade.Calamity) return null;
                if (toNext < 0)
                    return "JJK_GradeXpMaxed".Translate(xp);
                return "JJK_GradeXpProgress".Translate(xp, toNext);
            }
        }
    }
}
