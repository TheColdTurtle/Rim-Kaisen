using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static partial class JJK_DebugMahoragaChecks
    {
        private const string Folder = "D:/Modding/Rimworld/JJK-Mod-Main/QA/Mahoraga";
        private static Pawn Make(Map map, IntVec3 at)
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_Mahoraga"), Faction.OfPlayer, forceGenerateNewPawn: true));
            foreach (var h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(at, map, 3), map);
            return pawn;
        }
        private static void Hurt(Pawn p, DamageDef def, Pawn attacker, float amount = 5)
        {
            var hit = new DamageInfo(def, amount, 100, -1, attacker, p.RaceProps.body.corePart);
            hit.SetIgnoreArmor(true); p.TakeDamage(hit);
        }
        private static void Turns(Comp_MahoragaAdaptation c, int ticks) { for (int i = 0; i < ticks; i++) c.CompTick(); }

        [DebugAction("Rim-Kaisen", "Run Mahoraga adaptation checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Run()
        {
            var log = new List<string>(); var made = new List<Pawn>(); var objects = new List<Thing>();
            int passed = 0, failed = 0;
            Action<bool, string> check = (ok, name) => { if (ok) passed++; else failed++; var line = (ok ? "PASS " : "FAIL ") + name; log.Add(line); Log.Message("[JJK Mahoraga QA] " + line); };
            Map map = Find.CurrentMap;
            try
            {
                IntVec3 at = JJK_DebugAdvancedPuppetChecks.Clear(map);
                var caster = JJK_DebugCampaignBalance.Human(map, at, "Mahoraga QA caster", SorcererGrade.Calamity, "Bottomless", "Limitless"); made.Add(caster);
                var p = Make(map, at); made.Add(p); var c = p.TryGetComp<Comp_MahoragaAdaptation>();
                string bullet = MahoragaPhenomena.Conventional(DamageDefOf.Bullet), bomb = MahoragaPhenomena.Conventional(DamageDefOf.Bomb), cut = MahoragaPhenomena.Conventional(DamageDefOf.Cut);
                foreach (var d in DefDatabase<MahoragaPhenomenonDef>.AllDefsListForReading)
                    check(d.spins >= 0 && d.delayTicks >= 0, "valid tuning: " + d.defName);
                check(bullet != "JJK_Adapt_Lightning" && bomb != "JJK_Adapt_Purple" && cut != "JJK_Adapt_Shrine", "conventional and technique identities separate");
                check(MahoragaPhenomena.ForAbility(DefDatabase<AbilityDef>.GetNamed("JJK_Dismantle")) == MahoragaPhenomena.ForAbility(DefDatabase<AbilityDef>.GetNamed("JJK_Cleave")), "Cleave and Dismantle share phenomenon");
                check(MahoragaPhenomena.ForAbility(DefDatabase<AbilityDef>.GetNamed("JJK_DivineFlame")) != "JJK_Adapt_Shrine", "Divine Flame remains separate");
                Hurt(p, DamageDefOf.Bullet, caster);
                check(c.PendingTicks(bullet) == 420 && c.GetLevel(bullet) == 0, "survived bullet starts seven-second window");
                Turns(c, 419); check(c.GetLevel(bullet) == 0 && c.PendingTicks(bullet) == 1, "no early adaptation");
                Hurt(p, DamageDefOf.Bullet, caster); check(c.PendingTicks(bullet) == 1, "burst does not restart countdown");
                Turns(c, 1); check(c.GetLevel(bullet) == 1 && !p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "first spin completely heals");
                check(Mathf.Approximately(c.DamageFactor(bullet), 2f / 3f) && c.BlocksDeath(bullet), "basic first spin reduces damage and protects death");
                p.Kill(new DamageInfo(DamageDefOf.Bullet, 10000, instigator: caster)); check(!p.Dead, "real Pawn.Kill canceled for learned basic damage");
                Turns(c, 1000); check(c.GetLevel(bullet) == 1, "no second spin without further exposure");
                Hurt(p, DamageDefOf.Bullet, caster); Turns(c, 420); Hurt(p, DamageDefOf.Bullet, caster); Turns(c, 420);
                var immuneResult = p.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 10000, instigator: caster));
                check(c.IsImmune(bullet) && immuneResult.totalDamageDealt == 0 && !p.Dead, "third spin absorbs real bullets");
                c.Adapt(bomb);
                using (new MahoragaExposure("JJK_Adapt_Purple", caster)) Hurt(p, DamageDefOf.Bomb, caster);
                check(c.PendingTicks("JJK_Adapt_Purple") > 0 && p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "grenade adaptation does not stop Purple damage");
                c.Adapt("JJK_Adapt_Purple"); check(!c.BlocksDeath("JJK_Adapt_Purple"), "complex first spin remains vulnerable");
                var doomed = Make(map, at); made.Add(doomed); doomed.TryGetComp<Comp_MahoragaAdaptation>().Adapt("JJK_Adapt_Purple");
                using (new MahoragaExposure("JJK_Adapt_Purple", caster)) Hurt(doomed, DamageDefOf.Bomb, caster, 10000);
                check(doomed.Dead, "overwhelming real complex hit kills through partial adaptation");
                var fresh = Make(map, at); made.Add(fresh);
                Hurt(fresh, DamageDefOf.Bullet, caster, 10000); check(fresh.Dead, "unadapted overwhelming conventional hit kills");
                var limb = p.health.hediffSet.GetNotMissingParts().First(b => b.def.defName.Contains("Paw"));
                p.health.AddHediff(HediffDefOf.MissingBodyPart, limb);
                p.health.AddHediff(HediffDefOf.BloodLoss).Severity = .2f;
                using (new MahoragaExposure("JJK_Adapt_Shrine", caster)) Hurt(p, DamageDefOf.Cut, caster);
                c.Adapt("JJK_Adapt_Red");
                check(!p.health.hediffSet.PartIsMissing(limb) && !p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any() && !p.health.hediffSet.HasHediff(HediffDefOf.BloodLoss), "spin restores limb, unrelated wounds and blood loss");
                using (new MahoragaExposure(MahoragaPhenomena.Void, caster)) p.stances.stunner.StunFor(180, caster, false, true);
                check(c.PendingTicks(MahoragaPhenomena.Void) > 0 && p.stances.stunner.Stunned, "non-damaging Void stun starts adaptation");
                c.Adapt(MahoragaPhenomena.Void); p.stances.stunner.StopStun();
                using (new MahoragaExposure(MahoragaPhenomena.Void, caster)) p.stances.stunner.StunFor(180, caster, false, true);
                check(p.stances.stunner.StunTicksLeft == 120, "partial Void adaptation shortens stun");
                c.Adapt(MahoragaPhenomena.Void); c.Adapt(MahoragaPhenomena.Void);
                using (new MahoragaExposure(MahoragaPhenomena.Void, caster)) p.stances.stunner.StunFor(180, caster, false, true);
                check(!p.stances.stunner.Stunned, "completed Void adaptation ends and refuses stun");
                c.Adapt("JJK_Adapt_Roots"); var root = ExpansionTech.Buff(p, "JJK_RootSnare", 180);
                check(root?.TryGetComp<HediffComp_Disappears>()?.ticksToDisappear == 120, "root bind duration follows adaptation");
                c.Adapt("JJK_Adapt_Roots"); c.Adapt("JJK_Adapt_Roots"); ExpansionTech.Buff(p, "JJK_RootSnare", 180);
                check(!p.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_RootSnare")), "fully adapted roots removed and cannot reapply");
                c.Adapt("JJK_Adapt_Blue"); c.Adapt("JJK_Adapt_Blue"); c.Adapt("JJK_Adapt_Blue");
                IntVec3 before = p.Position;
                p.SetFaction(Faction.OfAncientsHostile);
                using (new MahoragaExposure("JJK_Adapt_Blue", caster)) JJK_LimitlessUtil.PullToward(caster, p.Position + IntVec3.East, 4, 3, 0, DamageDefOf.Blunt);
                check(p.Position == before, "Blue immunity prevents displacement");
                p.SetFaction(Faction.OfPlayer);
                caster.health.AddHediff(JJK_DefOf.JJK_Infinity);
                p.Position = CellFinder.RandomClosewalkCellNear(caster.Position, map, 1);
                if (!p.Position.AdjacentTo8WayOrInside(caster.Position)) p.Position = caster.Position;
                Hurt(caster, DamageDefOf.Cut, p);
                check(c.PendingTicks(MahoragaPhenomena.Infinity) > 0, "blocked physical contact teaches Infinity");
                for (int i = 0; i < 3; i++) c.Adapt(MahoragaPhenomena.Infinity);
                float injuryBefore = caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
                Hurt(caster, DamageDefOf.Cut, p);
                check(caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) > injuryBefore, "adapted Mahoraga contact pierces Infinity");
                var other = Make(map, at); made.Add(other); other.Position = p.Position;
                injuryBefore = caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
                Hurt(caster, DamageDefOf.Cut, other);
                check(Mathf.Approximately(injuryBefore, caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity)), "another summon does not inherit Infinity bypass");
                caster.health.RemoveHediff(caster.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_Infinity));
                check(MahoragaExposure.Current == null, "all completed scopes restored");
                try { using (new MahoragaExposure("JJK_Adapt_Sphere", caster)) throw new InvalidOperationException("QA scope"); } catch (InvalidOperationException) { }
                check(MahoragaExposure.Current == null, "exception restores attribution scope");
                using (new MahoragaExposure("JJK_Adapt_Purple", caster))
                {
                    check(MahoragaPhenomena.Resolve(new DamageInfo(DamageDefOf.Bullet, 1, instigator: other)) == bullet, "unrelated nested instigator retains conventional identity");
                    var projectile = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Bullet_AssaultRifle"), at, map); objects.Add(projectile);
                    projectile.Launch(caster, p, p, ProjectileHitFlags.IntendedTarget);
                    check(MahoragaDelivery.ProjectileKey(projectile) == "JJK_Adapt_Purple", "launched payload retains source beyond cast");
                }
                var nue = (Projectile)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_Proj_NueLightning")); objects.Add(nue);
                check(MahoragaDelivery.ProjectileKey(nue) == "JJK_Adapt_Lightning", "natural Nue projectile identifies cursed lightning");
                // Real ability override and Harmony scope, not just a hand-built DamageInfo.
                var victim = Make(map, at); made.Add(victim); victim.SetFaction(Faction.OfAncientsHostile);
                for (int i = 0; i < 3; i++) victim.TryGetComp<Comp_MahoragaAdaptation>().Adapt(bomb);
                var purple = JJK_DebugCampaignBalance.Learn(caster, DefDatabase<AbilityDef>.GetNamed("JJK_HollowPurple"));
                victim.Position = caster.Position + IntVec3.East * 2; purple.curTarget = victim; purple.Start(false);
                check(victim.Dead || victim.TryGetComp<Comp_MahoragaAdaptation>().PendingTicks("JJK_Adapt_Purple") > 0, "actual Hollow Purple Start bypasses conventional blast immunity");
                check(MahoragaExposure.Current == null, "ability Harmony finalizers restore scope");
                check(c.AdaptationDescription().Contains("Infinity") && c.AdaptationDescription().Contains("3/3"), "details list named phenomena and stages");
                check(c.CompInspectStringExtra().Split('\n').Length <= 6 && c.CompGetGizmosExtra().Any(), "compact inspection offers full scrollable wheel details");
                check(other.TryGetComp<Comp_MahoragaAdaptation>().GetLevel(bullet) == 0, "new summon begins without prior bullet adaptation");
            }
            catch (Exception ex) { failed++; log.Add("EXCEPTION " + ex); Log.Error("[JJK Mahoraga QA] " + ex); }
            finally
            {
                foreach (var thing in objects) if (!thing.Destroyed) thing.Destroy();
                foreach (var pawn in made) { if (pawn.Corpse != null) pawn.Corpse.Destroy(); else if (!pawn.Destroyed) pawn.Destroy(); }
                log.Add($"RESULT {passed} passed, {failed} failed");
                Directory.CreateDirectory(Folder); File.WriteAllLines(Path.Combine(Folder, "runtime-checks.txt"), log);
                Log.Message("[JJK Mahoraga QA] " + log.Last());
            }
        }
    }
}
