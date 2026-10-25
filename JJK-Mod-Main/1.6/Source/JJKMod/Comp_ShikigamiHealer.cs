using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public class CompProperties_ShikigamiHealer : CompProperties
    {
        public float radius = 6.5f;
        public float healPerPulse = 6f;   // total injury severity healed per pulse
        public int pulseIntervalTicks = 250;

        public CompProperties_ShikigamiHealer()
        {
            compClass = typeof(Comp_ShikigamiHealer);
        }
    }

    // Round Deer: continuously mends the wounds of nearby allies via reverse
    // cursed technique.
    public class Comp_ShikigamiHealer : ThingComp
    {
        private int ticksToPulse;

        private CompProperties_ShikigamiHealer Props => (CompProperties_ShikigamiHealer)props;
        private Pawn Pawn => parent as Pawn;

        public override void CompTick()
        {
            if (--ticksToPulse > 0) return;
            ticksToPulse = Props.pulseIntervalTicks;
            Pulse();
        }

        private void Pulse()
        {
            Pawn deer = Pawn;
            if (deer == null || !deer.Spawned || deer.Map == null || deer.Faction == null) return;

            if (deer.Map != null) FleckMaker.ThrowMetaIcon(deer.Position, deer.Map, FleckDefOf.Heart);

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(deer.Position, deer.Map, Props.radius, true))
            {
                if (!(thing is Pawn ally) || ally.Dead) continue;
                if (ally.Faction != deer.Faction) continue;
                if (ally.health == null) continue;
                HealOne(ally, Props.healPerPulse);
            }
        }

        private static void HealOne(Pawn pawn, float pool)
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count && pool > 0f; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && injury.Severity > 0f && injury.def.tendable)
                {
                    float amount = Mathf.Min(pool, injury.Severity);
                    injury.Heal(amount);
                    pool -= amount;
                }
            }
        }
    }
}
