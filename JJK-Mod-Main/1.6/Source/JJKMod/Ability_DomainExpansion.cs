using RimWorld;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Domain Expansion: an instant self-cast (no targeting, like Reverse Cursed
    // Technique) that manifests the user's innate domain centred on themselves.
    // All ongoing behaviour - the sure-hit pulse, the in-domain buff, upkeep, and
    // burnout - is owned by MapComponent_DomainManager; this just validates and
    // hands off. Grade gating and the "no double-domain / not during burnout"
    // rules live on the AbilityTierDef (minLevel + cannotBeActiveWithHediffs).
    public class Ability_DomainExpansion : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            DomainExtension ext = def?.GetModExtension<DomainExtension>();
            Map map = pawn?.Map;
            if (ext == null || map == null) return;

            // The reserve must be deep enough to manifest a domain (Section 20.2).
            if (pawn.GetStatValue(JJK_DefOf.JJK_MaxCursedEnergy) < ext.minMaxCE)
            {
                if (pawn.Spawned)
                {
                    MoteMaker.ThrowText(pawn.DrawPos, map,
                        "JJK_DomainNeedsReserve".Translate(ext.minMaxCE.ToString("F0")),
                        3.5f);
                }
                return; // Abort before base.Start so no energy/cooldown is spent.
            }

            // Mastery cast-gate: a full Domain Expansion can only be manifested once
            // the sorcerer has practiced the technique enough (accumulated mastery
            // XP). Grade unlocks the ability; mastery earns the right to use it.
            // Simple Domain is exempt (it is a scroll-learned defensive skill). The
            // enemy AI opens domains via the manager directly and is not gated here.
            if (!ext.isSimpleDomain && ext.masteryToOpen > 0f
                && def.abilityTrees != null && def.abilityTrees.Count > 0)
            {
                int mastery = GameComponent_JJK.Instance?.GetTechniqueXp(pawn, def.abilityTrees[0]) ?? 0;
                if (mastery < ext.masteryToOpen)
                {
                    if (pawn.Spawned)
                    {
                        MoteMaker.ThrowText(pawn.DrawPos, map,
                            "JJK_DomainNotMastered".Translate(mastery, ext.masteryToOpen.ToString("F0")),
                            3.5f);
                    }
                    return; // Abort before base.Start so no energy/cooldown is spent.
                }
            }

            base.Start(consumeEnergy);

            map.GetComponent<MapComponent_DomainManager>()?.Open(pawn, def);
        }
    }
}
