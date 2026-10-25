using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // ===================================================================== //
    // Signature moves added to existing techniques:
    //   Frame Freeze (Projection)   - instant touch-freeze stun.
    //   Top Speed Punch (Projection) - dash between frames and strike.
    //   Line Segment (Ratio)         - mark a 7:3 weak point (damage taken up).
    //   "Explode" (Cursed Speech)    - the heaviest word; the listener detonates.
    // ===================================================================== //

    // Generic instant single-target stun; duration read from the extension.
    public class StunAbilityExtension : DefModExtension
    {
        public int stunTicks = 90;
    }

    public class Ability_StunTarget : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead || target.stances?.stunner == null) return;
            int ticks = def.GetModExtension<StunAbilityExtension>()?.stunTicks ?? 90;
            target.stances.stunner.StunFor(ticks, pawn, true, true);
        }
    }

    // Top Speed Punch: move between the frames - blink adjacent to the target and
    // drive a single massive blow into it.
    public class Ability_TopSpeedPunch : Ability
    {
        private const float BaseDamage = 40f;
        private const float Chunk = 14f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target.Dead || !target.Spawned) return;

            // Dash: step to a free cell beside the target.
            IntVec3 dest = CellFinder.StandableCellNear(target.Position, map, 1.9f);
            if (dest.IsValid && dest != pawn.Position)
            {
                pawn.Position = dest;
                pawn.Notify_Teleported(false, true);
            }

            float total = BaseDamage * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / Chunk));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Chunk, 0.4f, -1f, pawn));
            }
            if (target.Spawned) FleckMaker.ThrowMicroSparks(target.DrawPos, map);
        }
    }

    // Line Segment: divide the target at 7:3, forcing open a weak point. The mark
    // raises all damage the target takes while it lasts.
    public class Ability_LineSegment : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead || target.health == null) return;

            HediffDef mark = JJK_DefOf.JJK_RatioMark;
            if (mark == null) return;
            Hediff existing = target.health.hediffSet.GetFirstHediffOfDef(mark);
            if (existing == null)
            {
                target.health.AddHediff(mark);
            }
            else
            {
                // Refresh the mark's duration.
                var disappears = existing.TryGetComp<HediffComp_Disappears>();
                if (disappears != null) disappears.ticksToDisappear = disappears.Props.disappearsAfterTicks.max;
            }

            if (target.Spawned)
                MoteMaker.ThrowText(target.DrawPos, target.Map, "JJK_RatioMarked".Translate());
        }
    }

    // "Explode": the heaviest cursed word. The listener's body detonates - massive
    // damage to the target and a small blast around it. Savage tongue strain.
    public class Ability_SpeechExplode : Ability_CursedSpeech
    {
        private const float BaseDamage = 70f;
        private const float BlastRadius = 2.4f;

        public override void Start(bool consumeEnergy = true)
        {
            float f = Factor;
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target.Dead) return;

            DealChunks(target, BaseDamage * f, DamageDefOf.Blunt, 0.6f);

            if (target.Spawned)
            {
                GenExplosion.DoExplosion(target.Position, map, BlastRadius, DamageDefOf.Bomb,
                    pawn, Mathf.RoundToInt(15f * f));
            }
        }
    }
}
