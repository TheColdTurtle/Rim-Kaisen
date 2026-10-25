using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // ===================================================================== //
    // Straw Doll Technique (Nobara). Nail-stacking finisher loop:
    //   Nail Volley / Nail Scatter embed cursed nails (a stacking counter that
    //   decays over time) but do little damage themselves. Hairpin then detonates
    //   the nails on a target, dealing damage scaled by how many are embedded -
    //   and an advanced Hairpin detonates every nailed pawn on the map at once.
    // ===================================================================== //

    // Carried by nail projectiles: how many nails a hit embeds.
    public class NailProjectileExtension : DefModExtension
    {
        public int nails = 1;
    }

    // Carried by a Hairpin ability: damage dealt per embedded nail, and whether
    // it detonates every nailed pawn on the map (advanced) or just the target.
    public class HairpinExtension : DefModExtension
    {
        public float damagePerNail = 9f;
        public bool mapWide = false;
    }

    public static class NailUtil
    {
        // Damage per stab chunk when nails erupt. Total Hairpin damage is split
        // into chunks this size across random body parts (nails bursting out),
        // so a big nail count actually maims rather than over-killing one part.
        private const float ChunkSize = 10f;

        public static void AddNails(Pawn pawn, int count)
        {
            if (pawn == null || pawn.Dead || pawn.health == null || count <= 0) return;
            HediffDef def = JJK_DefOf.JJK_NailsEmbedded;
            if (def == null) return;
            count = MahoragaPhenomena.ControlTicks(pawn, count, "JJK_Adapt_Nails");
            if (count <= 0) return;

            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(def, pawn);
                h.Severity = count;
                pawn.health.AddHediff(h);
            }
            else
            {
                h.Severity += count;  // Hediff.Severity setter clamps to maxSeverity.
            }
        }

        public static int GetNailCount(Pawn pawn)
        {
            if (pawn?.health == null) return 0;
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_NailsEmbedded);
            return h == null ? 0 : Mathf.RoundToInt(h.Severity);
        }

        public static void ClearNails(Pawn pawn)
        {
            if (pawn?.health == null) return;
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_NailsEmbedded);
            if (h != null) pawn.health.RemoveHediff(h);
        }

        // Detonate the embedded nails on one pawn. Returns the nail count consumed
        // (0 if there were none). Damage scales with nail count and CT output.
        public static int Detonate(Pawn target, Pawn instigator, float damagePerNail, float output)
        {
            using var exposure = new MahoragaExposure("JJK_Adapt_Resonance", instigator);
            int nails = GetNailCount(target);
            if (nails <= 0) return 0;

            float total = nails * damagePerNail * Mathf.Max(0.5f, output);
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / ChunkSize));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                var dinfo = new DamageInfo(DamageDefOf.Stab, ChunkSize, 0.3f, -1f, instigator);
                target.TakeDamage(dinfo);
            }

            if (target.Spawned)
                FleckMaker.ThrowMicroSparks(target.DrawPos, target.Map);
            ClearNails(target);
            return nails;
        }
    }

    // Shows the embedded nail count in brackets on the hediff label, and decays
    // (severity-per-day) so nails eventually fall away if not detonated.
    public class HediffComp_NailStack : HediffComp
    {
        public override string CompLabelInBracketsExtra => Mathf.RoundToInt(parent.Severity).ToString();
    }

    public class HediffCompProperties_NailStack : HediffCompProperties
    {
        public HediffCompProperties_NailStack() { compClass = typeof(HediffComp_NailStack); }
    }

    // Single-target nail: small stab damage, then embeds nails. Volley fires several
    // of these per cast (burstShotCount), stacking the counter quickly.
    public class Projectile_Nail : Bullet
    {
        public override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Pawn shooter = Launcher as Pawn;
            base.Impact(hitThing, blockedByShield);
            if (hitThing is Pawn p && !p.Dead)
            {
                int nails = def.GetModExtension<NailProjectileExtension>()?.nails ?? 1;
                if (shooter == null || p == shooter || p.HostileTo(shooter))
                    NailUtil.AddNails(p, nails);
            }
        }
    }

    // Area nail burst: a small explosion that embeds nails in every hostile pawn
    // caught in the blast.
    public class Projectile_NailBurst : Projectile_Explosive
    {
        public override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 center = Position;
            float radius = def.projectile.explosionRadius;
            int nails = def.GetModExtension<NailProjectileExtension>()?.nails ?? 1;
            Pawn shooter = Launcher as Pawn;

            base.Impact(hitThing, blockedByShield);  // resolves the explosion + destroys us

            if (map == null) return;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                if (t is Pawn p && !p.Dead && p.Spawned
                    && (shooter == null || (p != shooter && p.HostileTo(shooter))))
                {
                    NailUtil.AddNails(p, nails);
                }
            }
        }
    }

    // Hairpin: detonate embedded nails. Single-target by default; advanced variant
    // (mapWide) detonates every nailed pawn on the map at once.
    public class Ability_Hairpin : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            if (pawn == null) return;

            HairpinExtension ext = def.GetModExtension<HairpinExtension>();
            float perNail = ext?.damagePerNail ?? 9f;
            bool mapWide = ext?.mapWide ?? false;
            float output = pawn.GetStatValue(JJK_DefOf.JJK_CTOutput);

            if (mapWide)
            {
                Map map = pawn.Map;
                if (map == null) return;
                List<Pawn> targets = map.mapPawns.AllPawnsSpawned
                    .Where(p => NailUtil.GetNailCount(p) > 0).ToList();

                int hit = 0;
                foreach (Pawn p in targets)
                    if (NailUtil.Detonate(p, pawn, perNail, output) > 0) hit++;

                MoteMaker.ThrowText(pawn.DrawPos, map,
                    hit > 0 ? "JJK_HairpinResonanceHit".Translate(hit)
                            : "JJK_HairpinNoNails".Translate());
            }
            else
            {
                Pawn target = curTarget.Pawn;
                if (target == null) return;
                int nails = NailUtil.Detonate(target, pawn, perNail, output);
                if (nails <= 0 && target.Spawned)
                    MoteMaker.ThrowText(target.DrawPos, target.Map, "JJK_HairpinNoNails".Translate());
            }
        }
    }
}
