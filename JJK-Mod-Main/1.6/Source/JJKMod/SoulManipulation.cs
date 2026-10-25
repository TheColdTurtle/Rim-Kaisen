using System;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public static class CursedTechUtil
    {
        // An arm tag alone does not prove that its hand survives. An installed
        // replacement arm supplies its own hand even though vanilla hides its children.
        public static int WorkingHandCount(Pawn p)
        {
            if (p?.health == null) return 0;
            int count = 0;
            foreach (BodyPartRecord part in p.health.hediffSet.GetNotMissingParts())
            {
                if (part.def == BodyPartDefOf.Hand && p.health.hediffSet.GetPartHealth(part) > 0f)
                    count++;
                else if (part.def?.tags?.Contains(BodyPartTagDefOf.ManipulationLimbCore) == true
                    && p.health.hediffSet.hediffs.OfType<Hediff_AddedPart>()
                        .Any(h => h.Part == part && h.def.addedPartProps.partEfficiency > 0f))
                    count++;
            }
            return count;
        }
    }

    // Transfigured souls: monstrous creatures reshaped from humans by Idle
    // Transfiguration. They serve the caster's faction, can be banked ("stored")
    // and redeployed, or hurled as a projectile weapon.
    public static class TransfiguredSoulUtil
    {
        private static PawnKindDef kindCached;
        private static bool kindResolved;

        public static PawnKindDef Kind
        {
            get
            {
                if (!kindResolved)
                {
                    kindCached = DefDatabase<PawnKindDef>.GetNamedSilentFail("JJK_TransfiguredSoul_Kind");
                    kindResolved = true;
                }
                return kindCached;
            }
        }

        public static bool IsOwnedSoul(Pawn p, Pawn owner)
        {
            return p != null && owner != null && p.kindDef == Kind && p.Faction == owner.Faction;
        }

        public static Pawn SpawnSoul(Pawn owner, IntVec3 cell)
        {
            if (owner?.Map == null || Kind == null) return null;
            Pawn soul = PawnGenerator.GeneratePawn(Kind, owner.Faction);
            if (soul.Faction != owner.Faction) soul.SetFaction(owner.Faction, owner);
            TrainFully(owner, soul);
            if (soul.playerSettings != null) soul.playerSettings.followDrafted = true;
            GenSpawn.Spawn(soul, cell, owner.Map);
            if (owner.Faction == null)
                soul.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
            FleckMaker.ThrowSmoke(cell.ToVector3Shifted(), owner.Map, 2f);
            return soul;
        }

        public static void Transfigure(Pawn caster, Pawn victim)
        {
            if (caster?.Map == null || victim == null) return;
            Map map = caster.Map;
            IntVec3 cell = victim.Position;
            string victimLabel = victim.LabelShortCap;

            FleckMaker.ThrowSmoke(victim.DrawPos, map, 2.5f);
            if (!victim.Destroyed) victim.Destroy(DestroyMode.Vanish);

            SpawnSoul(caster, cell);
            Messages.Message("JJK_SoulTransfigured".Translate(caster.LabelShortCap, victimLabel),
                new LookTargets(cell, map), MessageTypeDefOf.NeutralEvent, false);
        }

        private static void TrainFully(Pawn tamer, Pawn tamee)
        {
            if (tamee.training == null) return;
            foreach (TrainableDef td in DefDatabase<TrainableDef>.AllDefsListForReading)
            {
                if (tamee.training.CanAssignToTrain(td, out _).Accepted)
                {
                    tamee.training.SetWantedRecursive(td, true);
                    tamee.training.Train(td, tamer, complete: true);
                }
            }
        }
    }

    // Soul Distortion (Idle Transfiguration): a melee-range touch on the soul.
    // Against a humanlike enemy it transfigures them into a soul that serves you;
    // against a soul-aware target (JJK_SoulObserver) or a non-humanlike it is
    // resisted and instead inflicts armor-ignoring soul damage.
    public class Ability_SoulDistortion : Ability
    {
        private const float ResistDamage = 45f;
        private const float Chunk = 14f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target == pawn || target.Dead) return;

            bool humanlike = target.RaceProps?.Humanlike ?? false;
            bool soulAware = target.story?.traits?.HasTrait(JJK_DefOf.JJK_SoulObserver) ?? false;
            // Only HOSTILE humanlikes (standing or downed) can be transfigured - a
            // downed colonist or neutral must never be destroyed by a misclick.
            bool validTarget = target.HostileTo(pawn);

            if (humanlike && validTarget && !soulAware)
            {
                TransfiguredSoulUtil.Transfigure(pawn, target);
                return;
            }

            // Resisted: warp the soul directly - armor-ignoring damage.
            float total = ResistDamage * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            int chunks = Mathf.Max(1, Mathf.RoundToInt(total / Chunk));
            for (int i = 0; i < chunks; i++)
            {
                if (target.Dead) break;
                var d = new DamageInfo(DamageDefOf.Cut, Chunk, 0f, -1f, pawn);
                d.SetIgnoreArmor(true);
                target.TakeDamage(d);
            }
            if (target.Spawned)
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_SoulResisted".Translate());
        }
    }

    // Consume Soul: bank one of your own transfigured souls into storage so it can
    // be redeployed or hurled later.
    public class Ability_ConsumeSoul : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null) return;

            if (!TransfiguredSoulUtil.IsOwnedSoul(target, pawn))
            {
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_NotYourSoul".Translate());
                return;
            }

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return;
            gc.AddStoredSoul(pawn, 1);
            FleckMaker.ThrowSmoke(target.DrawPos, map, 1.5f);
            if (!target.Destroyed) target.Destroy(DestroyMode.Vanish);
            if (pawn.Spawned)
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "JJK_SoulStored".Translate(gc.GetStoredSouls(pawn)));
        }
    }

    // Unleash Soul: spend one stored soul. On a hostile pawn, hurl it as a heavy
    // projectile; on the ground or an ally, redeploy it as a fighting summon.
    public class Ability_UnleashSoul : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            Map map = pawn?.Map;
            if (gc == null || map == null) return;
            if (gc.GetStoredSouls(pawn) <= 0)
            {
                if (pawn.Spawned) MoteMaker.ThrowText(pawn.DrawPos, map, "JJK_NoStoredSouls".Translate());
                return;
            }

            Pawn targetPawn = curTarget.Pawn;
            if (targetPawn != null && targetPawn != pawn && targetPawn.HostileTo(pawn))
            {
                // Weaponize: hurl the soul as a projectile.
                if (!gc.ConsumeStoredSoul(pawn)) return;
                ThingDef projDef = DefDatabase<ThingDef>.GetNamedSilentFail("JJK_Proj_SoulHurl");
                if (projDef != null)
                {
                    Projectile proj = (Projectile)GenSpawn.Spawn(projDef, pawn.Position, map);
                    proj.Launch(pawn, pawn.DrawPos, targetPawn, targetPawn, ProjectileHitFlags.IntendedTarget);
                }
            }
            else
            {
                // Redeploy as a fighting summon at the target cell.
                if (!gc.ConsumeStoredSoul(pawn)) return;
                IntVec3 cell = curTarget.Cell.IsValid ? curTarget.Cell : pawn.Position;
                TransfiguredSoulUtil.SpawnSoul(pawn, CellFinder.RandomClosewalkCellNear(cell, map, 2));
            }
        }

        public override Func<string> CanBeActivatedValidator()
        {
            return delegate
            {
                if ((GameComponent_JJK.Instance?.GetStoredSouls(pawn) ?? 0) <= 0)
                    return "JJK_NoStoredSouls".Translate();
                return "";
            };
        }
    }

    // Body Reconstruction: turn Idle Transfiguration inward to reshape your own body
    // back together, healing injuries. Scales with CT output (grade).
    public class Ability_BodyReconstruction : Ability
    {
        private const float BasePool = 32f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            if (pawn?.health == null) return;

            float pool = BasePool * Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            bool healedAny = RCTHealUtil.HealInjuries(pawn, pool);

            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map,
                    healedAny ? "JJK_BodyReconstructed".Translate() : "JJK_RCTNothingToHeal".Translate(),
                    new Color(0.8f, 0.7f, 0.9f), 3f);
            }
        }
    }
}
