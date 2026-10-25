using System;
using System.Linq;
using RimWorld;
using Verse;

namespace JJKMod
{
    // A rolling game-day allowance, shared across trees and saved with the pawn.
    // Combat kills, training and study have their own rewards and do not use it.
    public class CastPracticeAllowance : IExposable
    {
        public const int Limit = 40;
        public int windowStart = -1;
        public int used;
        public bool TryConsume(int now)
        {
            if (windowStart < 0 || now - windowStart >= 60000)
            { windowStart = now; used = 0; }
            if (used >= Limit) return false;
            used++;
            return true;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref windowStart, "windowStart", -1);
            Scribe_Values.Look(ref used, "used");
        }
    }

    // Larger innate reserves recover faster, with diminishing returns. Equipment,
    // jackpot and temporary maximum-CE bonuses cannot inflate this multiplier.
    public class StatPart_ReserveRecovery : StatPart
    {
        public static float Factor(Pawn pawn)
        {
            float reserve = pawn?.genes?.GenesListForReading
                .Where(g => g.Active && g is Gene_CursedEnergyReserve)
                .Select(g => g.def.statOffsets.GetStatOffsetFromList(JJK_DefOf.JJK_MaxCursedEnergy))
                .DefaultIfEmpty(140f).Max() ?? 140f;
            return (float)Math.Sqrt(Math.Max(140f, Math.Min(450f, reserve)) / 140f);
        }
        public override void TransformValue(StatRequest req, ref float val)
        { if (req.Thing is Pawn pawn) val *= Factor(pawn); }
        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn pawn) || Factor(pawn) == 1f) return null;
            return "JJK_ReserveRecovery".Translate(Factor(pawn).ToString("0.00"));
        }
    }
}
