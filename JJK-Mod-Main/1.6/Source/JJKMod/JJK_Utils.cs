using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public static class JJK_Utils
    {
        public const string CursedTechniqueCategory = "JJK_CursedTechnique";

        private static List<GeneDef> cachedCTGenes;
        private static List<GeneDef> cachedBasicCTGenes;

        // The rare, powerful inherited / special-grade techniques. These are gated
        // to high-grade sorcerers so a weak apprentice never rolls, say, the King of
        // Curses' Shrine or the Gojo clan's Limitless.
        private static readonly HashSet<string> FlagshipTechniqueGenes = new HashSet<string>
        {
            "JJK_CT_Gene_Limitless",
            "JJK_CT_Gene_Shrine",
            "JJK_CT_Gene_TenShadows",
            "JJK_CT_Gene_IdleTransfiguration",
            "JJK_CT_Gene_SpiritManipulation",
            "JJK_CT_Gene_Copy", "JJK_CT_Gene_Extinguishment", "JJK_CT_Gene_Pachinko",
        };

        // All concrete cursed-technique genes (those in the JJK_CursedTechnique
        // gene category). Cached after first use.
        public static List<GeneDef> CursedTechniqueGenes
        {
            get
            {
                if (cachedCTGenes == null)
                {
                    cachedCTGenes = DefDatabase<GeneDef>.AllDefsListForReading
                        .Where(g => g.geneClass != typeof(Gene_CopiedTechnique) && g.displayCategory != null
                                    && g.displayCategory.defName == CursedTechniqueCategory)
                        .ToList();
                }
                return cachedCTGenes;
            }
        }

        // Cursed-technique genes minus the flagship ones - the pool weaker sorcerers
        // and wild pawns roll from.
        public static List<GeneDef> BasicCursedTechniqueGenes
        {
            get
            {
                if (cachedBasicCTGenes == null)
                {
                    cachedBasicCTGenes = CursedTechniqueGenes
                        .Where(g => !FlagshipTechniqueGenes.Contains(g.defName))
                        .ToList();
                }
                return cachedBasicCTGenes;
            }
        }

        // Returns true if the pawn already carries any cursed energy reserve gene
        // or a heavenly restriction (both share the reserve exclusion tag).
        public static bool HasReserveOrRestriction(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                var tags = genes[i].def.exclusionTags;
                if (tags != null && tags.Contains("JJK_CursedEnergyReserve")) return true;
            }
            return false;
        }

        // True if the pawn carries a heavenly restriction (Physical Perfection or
        // Body Fragility) - both share the JJK_HeavenlyRestriction exclusion tag.
        public static bool IsHeavenlyRestricted(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                var tags = genes[i].def.exclusionTags;
                if (tags != null && tags.Contains("JJK_HeavenlyRestriction")) return true;
            }
            return false;
        }

        // True if the pawn can pass through a Domain Expansion barrier and ignore
        // its sure-hit: a heavenly restriction WITH no cursed energy at all (the
        // Maki / Toji physical-perfection archetype). The heavenly-restriction gene
        // check is essential - a bare "no cursed energy" test would wrongly include
        // ordinary baseliners, who also have zero cursed energy. Body-fragility
        // heavenly restriction keeps cursed energy, so it is excluded here.
        public static bool CanPhaseDomainBarrier(Pawn pawn)
        {
            return pawn != null
                && IsHeavenlyRestricted(pawn)
                && pawn.GetStatValue(JJK_DefOf.JJK_MaxCursedEnergy) <= 0f;
        }

        // True if the pawn has a cursed energy reserve gene (and so can cast).
        public static bool HasReserveGene(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i] is Gene_CursedEnergyReserve) return true;
            }
            return false;
        }

        // True if the pawn already has any cursed technique gene.
        public static bool HasCursedTechnique(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                var cat = genes[i].def.displayCategory;
                if (cat != null && cat.defName == CursedTechniqueCategory) return true;
            }
            return false;
        }

        // Grant one random cursed technique from the pool. Returns false if none
        // exist or the pawn already has one.
        public static bool AddRandomCursedTechnique(Pawn pawn)
        {
            if (pawn?.genes == null) return false;
            var pool = CursedTechniqueGenes;
            if (pool.NullOrEmpty()) return false;
            pawn.genes.AddGene(pool.RandomElementByWeight(g => g.GetModExtension<TechniqueRarity>()?.weight ?? 1f), false);
            return true;
        }

        // Grant a sorcerer their technique kit: any guaranteed techniques, then
        // `count` distinct random ones from the basic pool (or the full pool,
        // including flagship techniques, when allowFlagship is true).
        public static void GrantCursedTechniques(Pawn pawn, int count, bool allowFlagship,
            List<GeneDef> guaranteed = null)
        {
            if (pawn?.genes == null) return;

            if (guaranteed != null)
            {
                foreach (GeneDef g in guaranteed)
                {
                    if (g != null && !PawnHasGene(pawn, g)) pawn.genes.AddGene(g, false);
                }
            }

            var pool = (allowFlagship ? CursedTechniqueGenes : BasicCursedTechniqueGenes)
                .Where(g => !PawnHasGene(pawn, g)).ToList();

            int remaining = Mathf.Max(0, count);
            while (remaining > 0 && pool.Count > 0)
            {
                GeneDef pick = pool.RandomElementByWeight(g => g.GetModExtension<TechniqueRarity>()?.weight ?? 1f);
                pawn.genes.AddGene(pick, false);
                pool.Remove(pick);
                remaining--;
            }
        }

        private static bool PawnHasGene(Pawn pawn, GeneDef def)
        {
            var genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i].def == def) return true;
            }
            return false;
        }

        // Random cursed genes at pawn generation (Section 9.3).
        //  - Sorcerers that already carry a reserve gene (from a faction xenotype,
        //    etc.) but no technique are given ONE random cursed technique, so
        //    faction sorcerers field a varied, randomized kit instead of a fixed one.
        //  - Pawns with no cursed genes at all roll the wild-pawn table below.
        public static void TryRollCursedGenes(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive) return;
            if (pawn?.genes == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            if (pawn.ageTracker == null || pawn.ageTracker.AgeBiologicalYears < 13) return;

            // Existing reserve holder (dev xenotype / quest): give a random technique
            // if they don't have one yet.
            if (HasReserveGene(pawn))
            {
                if (!HasCursedTechnique(pawn)) AddRandomCursedTechnique(pawn);
                return;
            }

            // Carries a heavenly restriction (no reserve): leave as-is.
            if (HasReserveOrRestriction(pawn)) return;

            // Faction sorcerers (jujutsu schools, curse-user cells): a pawnkind
            // flagged with PawnKindSorcererExtension reliably gets its tier reserve
            // and a technique. This is independent of the wild-roll setting - the
            // schools ARE sorcerers; the toggle only governs random wild pawns.
            PawnKindSorcererExtension kindExt = pawn.kindDef?.GetModExtension<PawnKindSorcererExtension>();
            if (kindExt != null)
            {
                if (kindExt.reserve != null && Rand.Value < kindExt.sorcererChance)
                {
                    pawn.genes.AddGene(kindExt.reserve, false);
                    GrantCursedTechniques(pawn, kindExt.techniqueCount,
                        kindExt.allowFlagshipTechniques, kindExt.guaranteedTechniques);
                    // Set the pawn to its pawnkind's grade (drives the grade hediff
                    // scaling, Max CE multiplier, and grade-gated abilities). The
                    // game component is absent during initial world-gen, in which
                    // case the pawn defaults to Grade 4 - acceptable for the few
                    // pregenerated pawns; raid/visit sorcerers get their grade.
                    if (kindExt.grade > SorcererGrade.Grade4)
                    {
                        GameComponent_JJK.Instance?.SetGrade(pawn, kindExt.grade);
                    }
                }
                return;
            }

            // Wild pawns only: a rare chance, gated by the player setting.
            if (RimKaisenMod.Settings != null && !RimKaisenMod.Settings.randomCursedGenesOnPawnGen) return;

            // Wild-pawn roll table. Sorcerers are rare; an average reserve is the
            // most common one a sorcerer is born with, with shallow rarer than that.
            // A cursed energy reserve is only useful with a technique to spend it on,
            // so every rolled sorcerer ALSO receives one random cursed technique - a
            // reserve gene is never granted alone.
            float roll = Rand.Value;
            float chance = RimKaisenMod.Settings?.naturalGeneChance ?? 0.07f;
            if (chance <= 0f) return;
            // Remap the existing table's 7% tail, retaining its reserve/HR proportions.
            // Leave the original roll untouched at defaults for seeded generation.
            if (chance != 0.07f) roll = 1f - (1f - roll) * (0.07f / chance);
            GeneDef reserve = null;

            if (roll < 0.93f) return;                                   // 93% non-sorcerer
            else if (roll < 0.972f) reserve = JJK_DefOf.JJK_AverageReserves;    // 4.2% (most common)
            else if (roll < 0.987f) reserve = JJK_DefOf.JJK_ShallowReserves;    // 1.5% (rarer than average)
            else if (roll < 0.996f) reserve = JJK_DefOf.JJK_DeepReserves;       // 0.9%
            else if (roll < 0.999f) reserve = JJK_DefOf.JJK_BottomlessReserves; // 0.3%
            else                                                        // 0.1% heavenly restriction
            {
                GeneDef hr = DefDatabase<GeneDef>.GetNamedSilentFail("JJK_PhysicalPerfection");
                if (hr != null) pawn.genes.AddGene(hr, false);
                return;
            }

            if (reserve != null)
            {
                pawn.genes.AddGene(reserve, false);
                // Wild pawns get a basic technique only - no inherited clan / special
                // techniques on a random wanderer.
                GrantCursedTechniques(pawn, 1, false);
            }
        }

        // XP awarded for a kill, scaled off the victim's combat power
        // (Grade 4 spirit ~4, Special spirit ~48; capped at 500).
        public static int GradeXpForKill(Pawn victim)
        {
            if (victim?.kindDef == null) return 1;
            float power = victim.kindDef.combatPower;
            return Mathf.Clamp(Mathf.RoundToInt(power * 0.1f), 1, 500);
        }

        // True if the pawn is a cursed spirit (a curse). Recognised by the
        // CursedSpiritExtension marker, or any Anomaly entity as a fallback - so the
        // anti-spirit edge on cursed tools and the exorcise drop both apply.
        public static bool IsCursedSpirit(Pawn p)
        {
            if (p?.def == null) return false;
            if (p.def.HasModExtension<CursedSpiritExtension>()) return true;
            return p.RaceProps != null && p.RaceProps.IsAnomalyEntity;
        }

        // How much cursed residue a curse leaves when exorcised: its marker's value,
        // or an auto-scale from its combat power (a weak curse ~1, a special ~22).
        public static int ResidueForExorcism(Pawn victim)
        {
            CursedSpiritExtension ext = victim?.def?.GetModExtension<CursedSpiritExtension>();
            float power = victim?.kindDef?.combatPower ?? 0f;
            int amount = ext != null && ext.residueOnExorcise >= 0 ? ext.residueOnExorcise
                : Mathf.Clamp(Mathf.RoundToInt(power * 0.05f), 1, 40);
            return Mathf.Max(0, Mathf.RoundToInt(amount * (RimKaisenMod.Settings?.residueDropFactor ?? 1f)));
        }
    }
}
