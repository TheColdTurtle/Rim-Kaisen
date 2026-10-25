using RimWorld;
using Verse;

namespace JJKMod
{
    // Low colony mood multiplies cursed energy regeneration (Section 5.2).
    //   mood < 35%  ->  x2
    //   mood < 15%  ->  x3
    public class StatPart_MoodCEBonus : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            float mult = GetMultiplier(req);
            if (mult != 1f) val *= mult;
        }

        public override string ExplanationPart(StatRequest req)
        {
            float mult = GetMultiplier(req);
            if (mult == 1f) return null;
            return "JJK_MoodCEBonus".Translate(mult.ToString("0.0"));
        }

        private float GetMultiplier(StatRequest req)
        {
            if (RimKaisenMod.Settings != null && !RimKaisenMod.Settings.moodScalesCERegen) return 1f;
            if (!(req.Thing is Pawn pawn) || pawn.needs?.mood == null) return 1f;
            float mood = pawn.needs.mood.CurLevelPercentage;
            if (mood < 0.15f) return 3f;
            if (mood < 0.35f) return 2f;
            return 1f;
        }
    }
}
