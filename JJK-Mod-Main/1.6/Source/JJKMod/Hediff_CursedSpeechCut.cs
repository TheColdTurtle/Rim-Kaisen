using Verse;

namespace JJKMod
{
    // Vanilla deliberately gives Tongue a zero bleedRate. Vocal strain needs its
    // own injury; changing the body-part def would alter every mod's tongue wounds.
    public class Hediff_CursedSpeechCut : Hediff_Injury
    {
        public override float BleedRate
        {
            get
            {
                if (pawn == null || pawn.Dead || !pawn.health.CanBleed || this.IsTended()
                    || this.IsPermanent() || ageTicks >= 90000 || Part == null
                    || Part.def.IsSolid(Part, pawn.health.hediffSet.hediffs)
                    || pawn.health.hediffSet.GetDirectlyAddedPartFor(Part) != null) return 0f;
                return Severity * def.injuryProps.bleedRate * pawn.RaceProps.bleedRateFactor;
            }
        }
    }
}
