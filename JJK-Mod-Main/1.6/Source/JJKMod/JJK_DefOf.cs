using RimWorld;
using Verse;

namespace JJKMod
{
    [DefOf]
    public static class JJK_DefOf
    {
        // Cursed energy stats
        public static StatDef JJK_MaxCursedEnergy;
        public static StatDef JJK_CursedEnergyRegenRateMultiplier;
        public static StatDef JJK_CursedEnergyUsageMultiplier;
        public static StatDef JJK_CTOutput;
        public static StatDef JJK_CTCooldownMultiplier;
        public static StatDef JJK_DomainSureHitResist;

        // Cursed energy resource + technique class (TMF)
        public static TaranMagicFramework.AbilityResourceDef JJK_CursedEnergy;
        public static TaranMagicFramework.AbilityClassDef JJK_CursedTechniques;

        // Mahoraga race (fast-path damage check)
        public static ThingDef JJK_Mahoraga;

        // Closed-domain barrier building.
        public static ThingDef JJK_DomainBarrier;

        // Grade + technique hediffs
        public static HediffDef JJK_SorcererGrade;
        public static HediffDef JJK_FlowingRedScale;
        public static HediffDef JJK_ShikigamiBond;
        public static HediffDef JJK_BlackFlashSurge;
        public static HediffDef JJK_TechniqueSealed;
        public static HediffDef JJK_Infinity;
        public static HediffDef JJK_SixEyesOrgan;
        public static HediffDef JJK_DomainActive;
        public static HediffDef JJK_SimpleDomainActive;
        public static HediffDef JJK_DomainBurnout;
        public static HediffDef JJK_NailsEmbedded;
        public static HediffDef JJK_RatioMark;
        public static HediffDef JJK_Convicted;
        public static HediffDef JJK_DeathPenalty;
        public static HediffDef JJK_TechniqueDisrupted;
        public static HediffDef JJK_SukunaInfluence;
        public static HediffDef JJK_CursedEnergySuppressed;
        // The two anti-domain counters beyond Simple Domain.
        public static HediffDef JJK_DomainAmplification;
        public static HediffDef JJK_FallingBlossomEmotion;

        // Reserve genes (used for pawn-gen rolling and detection)
        public static GeneDef JJK_ShallowReserves;
        public static GeneDef JJK_AverageReserves;
        public static GeneDef JJK_DeepReserves;
        public static GeneDef JJK_BottomlessReserves;
        public static GeneDef JJK_SixEyes;

        // Materials
        public static ThingDef JJK_CursedResidue;
        // Spawned by the "a finger surfaces" incident (JJK_Events.cs).
        public static ThingDef JJK_SukunaFinger;

        // Soul-sight trait: resists Idle Transfiguration's transfiguration.
        public static TraitDef JJK_SoulObserver;

        // Reverse Cursed Technique traits: the ordinary self-healer, and the rare
        // soul mender who can also reverse cursed energy into someone else.
        public static TraitDef JJK_RCT;
        public static TraitDef JJK_SoulMender;

        // Reading a technique scroll.
        public static JobDef JJK_ReadScroll;

        // Swearing a binding vow at the altar.
        public static JobDef JJK_SwearBindingVow;

        static JJK_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(JJK_DefOf));
        }
    }
}
