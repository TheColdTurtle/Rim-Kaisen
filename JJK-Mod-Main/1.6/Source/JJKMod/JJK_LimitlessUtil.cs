using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Shared spatial effects for the Limitless technique: pulling pawns toward a
    // point (Lapse: Blue), blasting them away (Reversal: Red), and a linear beam
    // (Hollow Purple). Knockback is a short "blink": the pawn is shoved up to N
    // cells along a direction, stopping at the last standable cell, then briefly
    // stunned.
    public static class JJK_LimitlessUtil
    {
        // Lapse: Blue - drag enemies within radius of center toward it, then hurt them.
        public static void PullToward(Pawn caster, IntVec3 center, float radius, int pullDist,
            float damage, DamageDef damageDef)
        {
            Map map = caster?.Map;
            if (map == null) return;

            foreach (Pawn p in EnemyPawnsAround(caster, center, radius))
            {
                Shove(p, center, pullDist, toward: true, caster);
                if (damage > 0f) p.TakeDamage(new DamageInfo(damageDef, damage, 0f, -1f, caster));
            }
            FleckMaker.Static(center, map, FleckDefOf.PsycastAreaEffect, radius);
        }

        // Reversal: Red - hurl enemies near the target away from the caster.
        public static void BlastAway(Pawn caster, IntVec3 center, float radius, int knockDist,
            float damage, DamageDef damageDef)
        {
            Map map = caster?.Map;
            if (map == null) return;

            foreach (Pawn p in EnemyPawnsAround(caster, center, radius))
            {
                Shove(p, caster.Position, knockDist, toward: false, caster);
                p.stances?.stunner?.StunFor(45, caster);
                if (damage > 0f) p.TakeDamage(new DamageInfo(damageDef, damage, 0.2f, -1f, caster));
            }
            FleckMaker.Static(center, map, FleckDefOf.ExplosionFlash, radius);
        }

        // Hollow Purple - a line of imaginary mass from the caster through the target
        // cell, annihilating everything along it: pawns, buildings/structures, and
        // plants alike (it tears through the environment, not just enemies).
        public static void LineBeam(Pawn caster, IntVec3 target, int maxRange,
            float damage, DamageDef damageDef)
        {
            Map map = caster?.Map;
            if (map == null) return;

            Vector3 dir = (target - caster.Position).ToVector3();
            if (dir == Vector3.zero) return;
            dir = dir.normalized;

            HashSet<Thing> hit = new HashSet<Thing>();
            IntVec3 last = caster.Position;
            for (int i = 1; i <= maxRange; i++)
            {
                IntVec3 c = (caster.Position.ToVector3Shifted() + dir * i).ToIntVec3();
                if (!c.InBounds(map)) break;
                if (c == last) continue;
                last = c;

                FleckMaker.Static(c, map, FleckDefOf.PsycastAreaEffect, 0.6f);

                foreach (Thing t in c.GetThingList(map).ToList())
                {
                    if (t == caster || !hit.Add(t)) continue;

                    if (t is Pawn p)
                    {
                        if (p.Faction != caster.Faction)
                        {
                            p.TakeDamage(new DamageInfo(damageDef, damage, 1f, -1f, caster));
                        }
                    }
                    else if (t.def.useHitPoints && (t is Building || t is Plant || t.def.IsEdifice()))
                    {
                        // Structures and flora in the path are torn apart.
                        t.TakeDamage(new DamageInfo(damageDef, damage, 1f, -1f, caster));
                    }
                }
            }
        }

        private static IEnumerable<Pawn> EnemyPawnsAround(Pawn caster, IntVec3 center, float radius)
        {
            Map map = caster.Map;
            return GenRadial.RadialDistinctThingsAround(center, map, radius, true)
                .OfType<Pawn>()
                .Where(p => p != caster && p.Faction != caster.Faction)
                .ToList();
        }

        // Move a pawn up to dist cells along a direction (toward or away from anchor),
        // stopping at the last open standable cell.
        private static void Shove(Pawn p, IntVec3 anchor, int dist, bool toward, Pawn instigator)
        {
            Map map = p?.Map;
            if (map == null || dist <= 0) return;
            dist = MahoragaPhenomena.ControlTicks(p, dist);

            Vector3 v = (p.Position - anchor).ToVector3();
            if (toward) v = -v;
            if (v == Vector3.zero && instigator != null) v = (p.Position - instigator.Position).ToVector3();
            if (v == Vector3.zero) return;
            v = v.normalized;

            IntVec3 dest = p.Position;
            for (int i = 1; i <= dist; i++)
            {
                IntVec3 c = (p.Position.ToVector3Shifted() + v * i).ToIntVec3();
                if (c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null) dest = c;
                else break;
            }
            if (dest != p.Position)
            {
                p.Position = dest;
                p.Notify_Teleported(false, true);
            }
        }
    }
}
