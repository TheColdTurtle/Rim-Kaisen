using System.Collections.Generic;
using Verse;

namespace JJKMod
{
    // Marks a pawnkind as a (baseliner) sorcerer. At generation, a pawn of this
    // kind receives the given cursed energy reserve gene plus a random cursed
    // technique with probability sorcererChance. This lets faction sorcerers - the
    // jujutsu schools and curse-user cells - reliably be sorcerers WITHOUT a
    // dedicated xenotype "race", while ordinary wild pawns only rarely roll one
    // (JJK_Utils.TryRollCursedGenes).
    public class PawnKindSorcererExtension : DefModExtension
    {
        // Which reserve the pawnkind is born with (its grade/strength tier).
        public GeneDef reserve;

        // Chance a given pawn of this kind is actually a sorcerer (1.0 = always).
        public float sorcererChance = 1f;

        // The sorcerer grade the pawn generates at, so a "grade 1 sorcerer" pawnkind
        // really is Grade 1 (its grade hediff stat scaling, larger Max CE, and the
        // grade-gated abilities all follow). Parsed from the enum name in XML.
        public SorcererGrade grade = SorcererGrade.Grade4;

        // How many random cursed techniques the pawn rolls. Elites field more than
        // one, so a Grade 1 / Special sorcerer brings a varied, dangerous kit.
        public int techniqueCount = 1;

        // Whether the random pool may include the rare "flagship" techniques
        // (Limitless, Shrine, Ten Shadows, Idle Transfiguration). Kept off for the
        // weaker kinds so a grade-4 apprentice can't roll Sukuna's Shrine.
        public bool allowFlagshipTechniques = false;

        // Specific cursed-technique genes this pawnkind always receives (in addition
        // to the random rolls), for kinds with a signature technique.
        public List<GeneDef> guaranteedTechniques;
    }
}
