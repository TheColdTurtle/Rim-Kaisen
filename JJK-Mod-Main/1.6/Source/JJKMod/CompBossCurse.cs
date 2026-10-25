using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // One scripted boss attack: hurl a projectile at the nearest hostile pawn.
    public class BossAttack
    {
        public ThingDef projectile;
        public float range = 20f;
        public int cooldownTicks = 300;
        public int burstCount = 1;
    }

    // Drives a named boss curse. Animal-intelligence curses cannot use TMF
    // abilities (those need genes/traits), so bosses cast through this comp:
    // a data-driven list of projectile attacks fired at the nearest hostile,
    // plus (optionally) a true Domain Expansion opened via the domain manager
    // when enough enemies close in.
    public class CompProperties_BossCurse : CompProperties
    {
        public List<BossAttack> attacks;

        // Domain to open (an AbilityDef carrying DomainExtension), if any.
        public AbilityDef domain;
        // Hostiles within domainTriggerRadius needed before the domain opens.
        public int domainTriggerCount = 2;
        public float domainTriggerRadius = 20f;

        public CompProperties_BossCurse() { compClass = typeof(CompBossCurse); }
    }

    public class CompBossCurse : ThingComp
    {
        private List<int> nextAttackTick;
        private int nextDomainCheckTick;

        public CompProperties_BossCurse Props => (CompProperties_BossCurse)props;
        private Pawn Boss => parent as Pawn;

        public override void CompTick()
        {
            Pawn boss = Boss;
            if (boss == null || !boss.Spawned || boss.Dead || boss.Downed) return;
            if (boss.TryGetComp<CompBossPattern>()?.Busy == true) return;
            if (!boss.IsHashIntervalTick(30)) return;
            Map map = boss.Map;
            int now = Find.TickManager.TicksGame;

            // Scripted projectile attacks.
            List<BossAttack> attacks = Props.attacks;
            if (attacks != null && attacks.Count > 0)
            {
                if (nextAttackTick == null || nextAttackTick.Count != attacks.Count)
                {
                    nextAttackTick = new List<int>();
                    for (int i = 0; i < attacks.Count; i++) nextAttackTick.Add(0);
                }

                for (int i = 0; i < attacks.Count; i++)
                {
                    if (now < nextAttackTick[i]) continue;
                    BossAttack atk = attacks[i];
                    if (atk.projectile == null) continue;

                    Pawn target = FindTarget(boss, atk.range);
                    if (target == null) continue;

                    for (int b = 0; b < Mathf.Max(1, atk.burstCount); b++)
                    {
                        Projectile proj = (Projectile)GenSpawn.Spawn(atk.projectile, boss.Position, map);
                        proj.Launch(boss, boss.DrawPos, target, target, ProjectileHitFlags.IntendedTarget);
                    }
                    nextAttackTick[i] = now + atk.cooldownTicks;
                    break; // one attack per pulse, so bosses don't dump everything at once
                }
            }

            // Domain Expansion when surrounded.
            if (Props.domain != null && now >= nextDomainCheckTick)
            {
                nextDomainCheckTick = now + 250;
                var manager = map.GetComponent<MapComponent_DomainManager>();
                if (manager != null && !manager.HasDomain(boss)
                    && CountHostilesNear(boss, Props.domainTriggerRadius) >= Props.domainTriggerCount)
                {
                    manager.Open(boss, Props.domain);
                }
            }
        }

        private static Pawn FindTarget(Pawn boss, float range)
        {
            Pawn best = null;
            float bestDist = range * range;
            var pawns = boss.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed || !p.HostileTo(boss)) continue;
                float d = p.Position.DistanceToSquared(boss.Position);
                if (d < bestDist && GenSight.LineOfSight(boss.Position, p.Position, boss.Map, true))
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        private static int CountHostilesNear(Pawn boss, float radius)
        {
            int count = 0;
            float r2 = radius * radius;
            var pawns = boss.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!p.Dead && !p.Downed && p.HostileTo(boss)
                    && p.Position.DistanceToSquared(boss.Position) <= r2)
                {
                    count++;
                }
            }
            return count;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref nextAttackTick, "jjk_bossNextAttackTicks", LookMode.Value);
            Scribe_Values.Look(ref nextDomainCheckTick, "jjk_bossNextDomainCheck", 0);
        }
    }

    // Phase transitions for boss curses. At an HP fraction threshold, either
    // transform into a stronger pawnkind (Dagon: Cursed Womb -> Dagon, spawned
    // at full health per the source's phase resets) or gain a power-up hediff
    // (Mahito: Instant Spirit Body of Distorted Killing).
    public class CompProperties_PhaseShift : CompProperties
    {
        public float hpFraction = 0.5f;
        public PawnKindDef becomesKind;
        public HediffDef gainHediff;
        public string transitionKey;   // translation key for the letter/message

        public CompProperties_PhaseShift() { compClass = typeof(CompPhaseShift); }
    }

    public class CompPhaseShift : ThingComp
    {
        private bool triggered;

        public CompProperties_PhaseShift Props => (CompProperties_PhaseShift)props;

        public override void CompTick()
        {
            if (triggered) return;
            Pawn pawn = parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            if (!pawn.IsHashIntervalTick(60)) return;

            float hp = pawn.health.summaryHealth.SummaryHealthPercent;
            if (hp > Props.hpFraction) return;
            triggered = true;

            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;

            if (Props.becomesKind != null)
            {
                // Transform: replace this pawn with the evolved form at full health.
                FleckMaker.ThrowSmoke(pawn.DrawPos, map, 4f);
                Pawn evolved = PawnGenerator.GeneratePawn(Props.becomesKind, pawn.Faction);
                GameComponent_JJK.Instance?.TransferControlledCurse(pawn, evolved);
                pawn.Destroy(DestroyMode.Vanish);
                GenSpawn.Spawn(evolved, pos, map, Rot4.Random);
                if (evolved.Faction == null
                    && evolved.mindState?.mentalStateHandler != null)
                {
                    evolved.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, forced: true, forceWake: true);
                }
                Notify(Props.transitionKey, evolved, map);
            }
            else if (Props.gainHediff != null && pawn.health.hediffSet.GetFirstHediffOfDef(Props.gainHediff) == null)
            {
                pawn.health.AddHediff(Props.gainHediff);
                FleckMaker.ThrowSmoke(pawn.DrawPos, map, 3f);
                Notify(Props.transitionKey, pawn, map);
            }
        }

        private static void Notify(string key, Pawn pawn, Map map)
        {
            if (key.NullOrEmpty()) return;
            Find.LetterStack.ReceiveLetter("JJK_BossPhaseLabel".Translate(),
                key.Translate(), LetterDefOf.ThreatBig, new LookTargets(pawn));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref triggered, "jjk_phaseTriggered", false);
        }
    }
}
