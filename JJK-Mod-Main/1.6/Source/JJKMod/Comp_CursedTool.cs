using Verse;

namespace JJKMod
{
    // Marks a weapon as a cursed tool. Carries the tuning for the melee damage
    // patch (Patch_CursedTool): how strongly the wielder's cursed-technique output
    // amplifies the tool's hits, and how much extra damage it deals to cursed
    // spirits (Anomaly entities). Bypassing spirit armor is handled by the tool's
    // own armorPenetration in XML; this comp adds the on-hit cursed-energy bonus.
    public class CompProperties_CursedTool : CompProperties
    {
        // Extra melee damage = dealt damage * (CTOutput - 1) * ceOutputScaling.
        // So a stronger sorcerer (higher grade) drives a cursed tool harder, while
        // a non-sorcerer (CTOutput defaults to 1) gets no bonus from this term.
        public float ceOutputScaling = 1f;

        // Extra damage multiplier applied when striking a cursed spirit.
        public float spiritBonusFactor = 0f;

        // Inverted Spear of Heaven: on hit, seal the target's cursed technique for
        // sealTicks (refreshed each strike). Future Limitless note: a sealed pawn's
        // Infinity is suppressed, so this is the weapon that can land on a Limitless
        // user (handled when Limitless is added).
        public bool sealsTechnique = false;
        public int sealTicks = 2500;

        // Inverted Spear of Heaven: its strikes pass through Limitless Infinity
        // (the only thing that can). Checked by Patch_Infinity off dinfo.Weapon.
        public bool bypassesInfinity = false;

        // Split Soul Katana: ONLY in the hands of a heavenly-restricted pawn does
        // this tool cut the soul - armor-ignoring bonus damage = dealt * factor.
        // A normal wielder deals ordinary weapon damage (factor unused).
        public float heavenlyRestrictionSoulFactor = 0f;

        // Nanami's blunt sword: bonus damage = dealt * factor against a target
        // carrying the 7:3 Ratio mark (Line Segment) - the weapon built to strike
        // the forced weak point.
        public float ratioMarkBonusFactor = 0f;

        // Nobara's hammer: each hit embeds this many cursed nails in the target
        // (fuel for the Straw Doll Technique's Hairpin).
        public int nailsPerHit = 0;

        // Kusakabe's katana: bonus damage = dealt * factor when the WIELDER has no
        // cursed technique - the sword of the school for the technique-less.
        public float noTechniqueBonusFactor = 0f;

        // Black Rope: on hit, DISRUPT the target's cursed technique - a hediff
        // that degrades their technique output and cursed energy regeneration
        // (weaker than the Inverted Spear's hard seal, but it stacks up over a
        // fight). Refreshed each strike.
        public bool disruptsTechnique = false;
        public int disruptTicks = 1800;

        // Kamutoke: each hit discharges lightning, briefly stunning the target.
        public int stunOnHitTicks = 0;

        // Okkotsu's katana: bonus damage scales with the wielder's MAX cursed
        // energy - bonus = dealt * (MaxCE / 200) * factor. Raw reserves, not
        // refinement, drive this blade.
        public float maxCEScalingFactor = 0f;

        // Slaughter Demon: each landed hit shoves the target back this many cells
        // (stopped early by walls or impassable terrain).
        public int knockbackCells = 0;

        // Hiten: a kill with this tool restores this much cursed energy to the
        // wielder, provided they are at least energyOnKillMinGrade.
        public float energyOnKill = 0f;
        public SorcererGrade energyOnKillMinGrade = SorcererGrade.Grade1;

        // Black Sword: a kill with this tool whips every shikigami bonded to the
        // wielder into a frenzy - the hediff is applied (or refreshed) on each of
        // their summons on the map for shikigamiFrenzyTicks.
        public HediffDef shikigamiFrenzyHediff;
        public int shikigamiFrenzyTicks = 1800;

        public CompProperties_CursedTool()
        {
            compClass = typeof(Comp_CursedTool);
        }
    }

    public partial class Comp_CursedTool : ThingComp
    {
        public CompProperties_CursedTool Props => (CompProperties_CursedTool)props;
    }
}
