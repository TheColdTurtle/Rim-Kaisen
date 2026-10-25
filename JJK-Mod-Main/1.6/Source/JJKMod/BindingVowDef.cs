using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JJKMod
{
    // A binding vow a sorcerer can swear at a binding-vow altar: a self-imposed
    // pact that trades a permanent restriction or sacrifice for greater cursed
    // power. Data-driven - a vow is just the hediff(s) it grants (which carry both
    // the benefit and the cost via their stage stat factors). More vow archetypes
    // (conditional buffs, technique sacrifices, Overtime bursts) slot in as more
    // BindingVowDefs without code changes.
    public class BindingVowDef : Def
    {
        public bool requiresProtectedPawn;
        public List<HediffDef> grantHediffs = new List<HediffDef>();
    }

    public static class BindingVowUtil
    {
        // True if the pawn is already bound by any binding vow (one vow per pawn).
        public static bool HasAnyVow(Pawn pawn)
        {
            if (pawn?.health == null) return false;
            List<BindingVowDef> vows = DefDatabase<BindingVowDef>.AllDefsListForReading;
            for (int i = 0; i < vows.Count; i++)
            {
                List<HediffDef> hediffs = vows[i].grantHediffs;
                for (int j = 0; j < hediffs.Count; j++)
                    if (pawn.health.hediffSet.HasHediff(hediffs[j])) return true;
            }
            return false;
        }

        // Seal the vow: apply each of its hediffs (the benefit-and-cost package).
        public static void ApplyVow(Pawn pawn, BindingVowDef vow, Pawn protectedPawn = null)
        {
            if (pawn?.health == null || vow?.grantHediffs == null || HasAnyVow(pawn)) return;
            if (vow.requiresProtectedPawn)
            {
                if (protectedPawn == null || protectedPawn == pawn || protectedPawn.Dead || protectedPawn.Faction != pawn.Faction) return;
                SorceryDevelopment.State(pawn).protectedPawn = protectedPawn;
            }
            foreach (HediffDef hd in vow.grantHediffs)
            {
                if (pawn.health.hediffSet.GetFirstHediffOfDef(hd) == null)
                    pawn.health.AddHediff(hd);
            }
            if (pawn.Spawned && pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                Messages.Message("JJK_VowSworn".Translate(pawn.LabelShortCap, vow.label),
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }
    }
}
