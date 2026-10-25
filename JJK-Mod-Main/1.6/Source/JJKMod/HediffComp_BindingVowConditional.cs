using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JJKMod
{
    // The condition under which a conditional binding vow's power is active.
    public enum VowCondition
    {
        LowHealth,    // active while badly wounded (health % below threshold)
        Alone,        // active while no allies stand nearby
        Outnumbered,  // active while at least 'threshold' hostiles are nearby
        NightTime,    // active during the night
        InOwnDomain,  // active while the sorcerer holds a Domain Expansion
        Overtime     // 18:00-08:00 local time; appended to preserve old enum values
    }

    public class HediffCompProperties_BindingVowConditional : HediffCompProperties
    {
        public VowCondition condition = VowCondition.LowHealth;
        public float threshold = 0.4f; // health fraction, or hostile count, by condition
        public float radius = 12f;     // proximity radius for Alone / Outnumbered

        public HediffCompProperties_BindingVowConditional()
        {
            compClass = typeof(HediffComp_BindingVowConditional);
        }
    }

    // Selects the stage below/above minSeverity 0.5 as the condition changes.
    // Each definition owns both stages: Overtime explicitly penalizes daytime,
    // while other vows may use a neutral dormant stage.
    public class HediffComp_BindingVowConditional : HediffComp
    {
        private const float ActiveSeverity = 1f;
        private const float DormantSeverity = 0.01f;

        private HediffCompProperties_BindingVowConditional Props =>
            (HediffCompProperties_BindingVowConditional)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.IsHashIntervalTick(60)) return;
            parent.Severity = ConditionMet(pawn) ? ActiveSeverity : DormantSeverity;
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            parent.Severity = ConditionMet(Pawn) ? ActiveSeverity : DormantSeverity;
        }

        public static bool IsOvertimeHour(int hour) => hour >= 18 || hour < 8;

        public override string CompTipStringExtra =>
            Props.condition == VowCondition.Overtime
                ? (parent.Severity >= 0.5f ? "JJK_VowOvertime" : "JJK_VowOnClock").Translate()
                : (parent.Severity >= 0.5f ? "JJK_VowActive" : "JJK_VowDormant").Translate();

        private bool ConditionMet(Pawn pawn)
        {
            switch (Props.condition)
            {
                case VowCondition.LowHealth:
                    return pawn.health != null
                        && pawn.health.summaryHealth.SummaryHealthPercent < Props.threshold;
                case VowCondition.Alone:
                    return pawn.Spawned && CountNear(pawn, hostile: false) == 0;
                case VowCondition.Outnumbered:
                    return pawn.Spawned && CountNear(pawn, hostile: true) >= Props.threshold;
                case VowCondition.NightTime:
                    if (!pawn.Spawned) return false;
                    int hour = GenLocalDate.HourInteger(pawn);
                    return hour >= 20 || hour < 6;
                case VowCondition.InOwnDomain:
                    return pawn.health != null
                        && pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_DomainActive) != null;
                case VowCondition.Overtime:
                    return IsOvertimeHour(GenLocalDate.HourInteger(pawn));
                default:
                    return false;
            }
        }

        // Count nearby spawned pawns - either same-faction allies, or hostiles.
        private int CountNear(Pawn pawn, bool hostile)
        {
            Map map = pawn.Map;
            if (map == null) return 0;
            float r2 = Props.radius * Props.radius;
            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == pawn || other.Dead || other.Downed) continue;
                bool isHostile = other.HostileTo(pawn);
                if (hostile != isHostile) continue;
                if (!hostile && other.Faction != pawn.Faction) continue; // allies only
                if ((other.Position - pawn.Position).LengthHorizontalSquared <= r2) count++;
            }
            return count;
        }
    }
}
