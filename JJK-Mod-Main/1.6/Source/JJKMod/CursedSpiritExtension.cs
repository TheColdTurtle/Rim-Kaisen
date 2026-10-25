using Verse;

namespace JJKMod
{
    // Marks a race as a cursed spirit (a curse) - the monsters jujutsu sorcerers
    // exist to exorcise. Carries the curse's grade (for flavor / scaling) and how
    // much cursed residue it leaves when exorcised. Detection goes through
    // JJK_Utils.IsCursedSpirit, so cursed tools' anti-spirit edge and the
    // exorcise-on-death drop both recognise these pawns.
    public class CursedSpiritExtension : DefModExtension
    {
        // The curse's grade (Grade4 = weakest ... Special = strongest).
        public SorcererGrade grade = SorcererGrade.Grade4;

        // Cursed residue dropped when exorcised. < 0 = auto-scale from combatPower.
        public int residueOnExorcise = -1;

        // Extra thing dropped when exorcised (boss curses drop a technique remnant
        // that can teach a colonist the curse's innate technique).
        public ThingDef bossDrop;

        // Unique named bosses are excluded from the generic curse spawn pools
        // (manifestation incident, corpse spawner, curse-summon budgets); they
        // only arrive through their own incident.
        public bool uniqueBoss;
    }
}
