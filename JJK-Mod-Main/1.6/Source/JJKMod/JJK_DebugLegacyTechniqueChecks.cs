using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugLegacyTechniqueChecks
    {
        [DebugAction("Rim-Kaisen", "Run legacy technique checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Run()
        {
            Map map = Find.CurrentMap;
            var made = new List<Pawn>();
            int passed = 0;
            var failures = new List<string>();
            Action<bool, string> check = (ok, label) => { if (ok) passed++; else failures.Add(label); Log.Message("[JJK Legacy QA] " + (ok ? "PASS " : "FAIL ") + label); };
            var fields = map.GetComponent<MapComponent_LegacyTechniques>();
            var blasts = map.GetComponent<MapComponent_DelayedBlasts>();
            try
            {
                var at = JJK_DebugAdvancedPuppetChecks.Clear(map);
                Pawn caster = JJK_DebugCampaignBalance.Human(map, at, "Legacy QA caster", SorcererGrade.Special, "Bottomless", "DisasterPlants"); made.Add(caster);
                Pawn enemy = JJK_DebugCampaignBalance.Human(map, at + IntVec3.East * 5, "Legacy QA enemy", SorcererGrade.Special, "Bottomless", "BloodManipulation"); made.Add(enemy);
                enemy.SetFaction(Faction.OfAncientsHostile);
                Pawn ally = JJK_DebugCampaignBalance.Human(map, at + IntVec3.North * 2, "Legacy QA ally", SorcererGrade.Grade4, "Average", "DisasterPlants"); made.Add(ally);
                Func<string, Ability_LegacyTechnique> learn = name => (Ability_LegacyTechnique)JJK_DebugCampaignBalance.Learn(caster, DefDatabase<AbilityDef>.GetNamed("JJK_" + name));
                foreach (string tree in new[] { "DisasterFlames", "DisasterTides", "BloodManipulation", "Shrine" }) caster.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_" + tree), true);
                foreach (var d in DefDatabase<AbilityDef>.AllDefsListForReading.Where(d => d.GetModExtension<LegacyMoveExtension>() != null))
                {
                    var a = JJK_DebugCampaignBalance.Learn(caster, d);
                    check(a is Ability_LegacyTechnique && a.AbilityTier != null && a.AbilityTier.verbProperties.NullOrEmpty(), d.defName + " uses the custom cast job");
                }
                check(!LegacyTechniqueUtility.Affected(caster, ally), "friendly pawns excluded from selective effects");
                enemy.health.AddHediff(JJK_DefOf.JJK_Infinity);
                check(!LegacyTechniqueUtility.Affected(caster, enemy), "Infinity rejects damage-free crowd control");
                enemy.health.RemoveHediff(enemy.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_Infinity));
                check(LegacyTechniqueUtility.Affected(caster, enemy), "hostile target is eligible after Infinity ends");

                var roots = learn("Roots"); roots.curTarget = enemy; roots.Start(false);
                check(fields.fields.Any(f => f.caster == caster && f.nextTick == ExpansionTech.Now + 60), "roots telegraph for one second");
                check(!enemy.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_RootSnare")), "roots do not snare before eruption");
                fields.fields.First(f => f.caster == caster).nextTick = ExpansionTech.Now; fields.MapComponentTick();
                check(enemy.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_RootSnare")), "root eruption applies movement snare");
                foreach (var h in enemy.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) enemy.health.RemoveHediff(h);
                var thicket = learn("HugeRoots"); thicket.curTarget = enemy; thicket.Start(false); thicket.Start(false);
                check(fields.fields.Count(f => f.ability == thicket.def && f.caster == caster) == 1, "recasting replaces the caster's thicket");
                check(fields.fields.First(f => f.caster == caster && f.ability == thicket.def).pulses == 6, "thicket has six bounded eruptions");
                caster.health.AddHediff(JJK_DefOf.JJK_CursedEnergySuppressed); fields.MapComponentTick();
                check(!fields.fields.Any(f => f.caster == caster), "sealing caster terminates sustained fields");
                caster.health.RemoveHediff(caster.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_CursedEnergySuppressed));

                var buds = learn("CursedBuds"); buds.curTarget = enemy; buds.Start(false);
                var parasite = enemy.health.hediffSet.hediffs.OfType<Hediff_CursedBuds>().FirstOrDefault();
                check(parasite != null, "cursed buds attach on completed hostile cast");
                var energy = ExpansionTech.Energy(enemy); energy.energy = 100;
                parasite.Feed(0); check(energy.energy == 100 && parasite.Severity == 1, "free controls do not feed buds");
                parasite.Feed(20); check(energy.energy == 95 && parasite.Severity == 2, "paid casts drain proportional CE and grow buds");
                parasite.Feed(200); check(energy.energy == 95 && parasite.Severity == 2, "buds cannot feed twice in the same second");
                buds.Start(false); check(enemy.health.hediffSet.hediffs.OfType<Hediff_CursedBuds>().Count() == 1, "bud recasts refresh without stacking parasites");

                var flowers = learn("FlowerField"); flowers.curTarget = enemy; flowers.Start(false);
                fields.fields.First(f => f.caster == caster && f.ability == flowers.def).nextTick = ExpansionTech.Now; fields.MapComponentTick();
                check(enemy.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_FlowerDistraction")), "flower field disrupts hostile accuracy");
                check(!ally.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_FlowerDistraction")), "flower field spares allies");
                var swarm = learn("DeathSwarm"); swarm.curTarget = enemy; swarm.Start(false);
                var zone = fields.fields.First(f => f.caster == caster && f.ability == swarm.def); zone.nextTick = ExpansionTech.Now; fields.MapComponentTick();
                check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_Proj_SwarmFish")).OfType<Projectile_LegacyFish>().Any(), "swarm emits physical fish projectiles");
                check(zone.pulses == 5, "swarm spends one of six volleys per pulse");
                JujutsuTraining.Class(caster).RemoveAbility(swarm); fields.MapComponentTick();
                check(!fields.fields.Contains(zone), "losing an ability ends its sustained swarm");

                var convergence = learn("Convergence"); convergence.Start(false);
                check(caster.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_Convergence")), "Convergence prepares blood");
                var supernova = learn("Supernova"); supernova.curTarget = at + IntVec3.North * 7;
                var preview = TechniquePreview.Cells(supernova, supernova.curTarget, out _);
                check(preview.Contains(supernova.curTarget.Cell + IntVec3.East * 4), "prepared Supernova preview expands to four cells");
                supernova.Start(false);
                var blast = blasts.PendingBlasts.Last();
                check(blast.radius == 4 && blast.triggerTick == ExpansionTech.Now + 90, "prepared Supernova schedules larger delayed blast");
                check(!caster.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_Convergence")), "Supernova consumes preparation exactly once");
                supernova.Start(false); check(blasts.PendingBlasts.Last().radius == 3, "unprepared follow-up returns to ordinary radius");
                var meteor = learn("MaximumMeteor"); meteor.curTarget = at + IntVec3.North * 8; meteor.Start(false);
                check(blasts.PendingBlasts.Last().triggerTick == ExpansionTech.Now + 240, "Meteor provides four seconds to escape");
                var insects = learn("EmberBurst"); insects.curTarget = at + IntVec3.North * 7; insects.Start(false);
                check(blasts.PendingBlasts.Last().damageDef == DamageDefOf.Flame && blasts.PendingBlasts.Last().triggerTick == ExpansionTech.Now + 90, "Ember Insects schedule flame detonation");
                var cleave = learn("Cleave"); check(!cleave.CanHitTarget(enemy), "Cleave rejects distant targets");
                int xp = GameComponent_JJK.Instance.GetTechniqueXp(caster, cleave.def.abilityTrees[0]);
                cleave.curTarget = enemy; cleave.Start(false);
                check(GameComponent_JJK.Instance.GetTechniqueXp(caster, cleave.def.abilityTrees[0]) == xp, "out-of-range direct Cleave grants no cast credit");
                check(!buds.CanHitTarget(ally), "cursed bud targeting rejects allies before spending energy");
                check(!roots.CanHitTarget(at + IntVec3.East * 30), "root targeting enforces its range");
                check(LegacyTechniqueUtility.CleaveDamage(1, 0, 1) == 24 && LegacyTechniqueUtility.CleaveDamage(1, 100, 100) == 48, "Cleave adapts within its damage cap");
                var before = enemy.Position; LegacyTechniqueUtility.Push(enemy, at, 3);
                check(enemy.Position == before + IntVec3.East * 3, "torrent displacement moves three open cells");
                check(LegacyTechniqueUtility.LineCells(caster, at + IntVec3.East * 6).Count == 6, "line attack covers the selected distance");
                caster.health.AddHediff(JJK_DefOf.JJK_DomainBurnout);
                int pending = blasts.PendingCount; meteor.Start(false);
                check(blasts.PendingCount == pending, "burnout blocks direct activation before scheduling damage");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                fields.fields.RemoveAll(f => made.Contains(f.caster));
                var pending = (List<MapComponent_DelayedBlasts.Pending>)AccessTools.Field(typeof(MapComponent_DelayedBlasts), "pending").GetValue(blasts);
                pending.RemoveAll(p => made.Contains(p.instigator));
                foreach (var p in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_Proj_SwarmFish")).OfType<Projectile>().Where(p => made.Contains(p.Launcher as Pawn)).ToList()) p.Destroy();
                foreach (var p in made) if (!p.Destroyed) p.Destroy();
            }
            Log.Message($"[JJK Legacy QA] RESULT {passed} passed, {failures.Count} failed. " + string.Join(" | ", failures));
        }

        private const string SnapshotFile = "D:/Modding/Rimworld/JJK-Mod-Main/QA/LegacyTechniques/persistence-expected.txt";
        [DebugAction("Rim-Kaisen", "Stage legacy persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StagePersistence()
        {
            Find.TickManager.Pause();
            Map map = Find.CurrentMap; var at = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var p = JJK_DebugCampaignBalance.Human(map, at, "Legacy persistence caster", SorcererGrade.Special, "Bottomless", "DisasterPlants");
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_DisasterTides"), true);
            foreach (var name in new[] { "HugeRoots", "FlowerField", "DeathSwarm" })
            {
                var a = (Ability_LegacyTechnique)JJK_DebugCampaignBalance.Learn(p, DefDatabase<AbilityDef>.GetNamed("JJK_" + name));
                a.curTarget = at + (name == "DeathSwarm" ? IntVec3.East : IntVec3.North) * 6; a.Start(false);
            }
            foreach (var f in map.GetComponent<MapComponent_LegacyTechniques>().fields.Where(f => f.caster == p)) f.nextTick = ExpansionTech.Now + 900;
            MapComponent_DelayedBlasts.Queue(map, at + IntVec3.South * 7, 900, 3, DamageDefOf.Flame, 20, p);
            ExpansionTech.Buff(p, "JJK_Convergence", 900);
            ((Hediff_CursedBuds)ExpansionTech.Buff(p, "JJK_CursedBuds", 900)).Feed(20);
            File.WriteAllText(SnapshotFile, Snapshot());
            Find.Selector.ClearSelection(); Find.Selector.Select(p); CameraJumper.TryJump(p);
            Log.Message("[JJK Legacy QA] Persistence staged: " + Snapshot());
        }
        private static string Snapshot()
        {
            var map = Find.CurrentMap;
            var p = map.mapPawns.AllPawnsSpawned.First(p => p.Name?.ToStringFull == "Legacy persistence caster");
            var fields = map.GetComponent<MapComponent_LegacyTechniques>().fields.Where(f => f.caster == p)
                .OrderBy(f => f.ability.defName).Select(f => f.ability.defName + ":" + f.cell + ":" + f.nextTick + ":" + f.pulses + ":" + f.power);
            var blasts = map.GetComponent<MapComponent_DelayedBlasts>().PendingBlasts.Where(b => b.instigator == p)
                .Select(b => b.cell + ":" + b.triggerTick + ":" + b.radius + ":" + b.damageAmount);
            return p.ThingID + "|" + string.Join(";", fields) + "|" + string.Join(";", blasts) + "|"
                + p.health.hediffSet.hediffs.OfType<Hediff_CursedBuds>().Single().Severity + "|"
                + p.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_Convergence"));
        }
        [DebugAction("Rim-Kaisen", "Verify legacy persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void VerifyPersistence()
        {
            bool match = File.ReadAllText(SnapshotFile) == Snapshot();
            Log.Message("[JJK Legacy QA] " + (match ? "PASS" : "FAIL") + " actual save/load preserves fields, fuses, caster references, bud growth and Convergence. " + Snapshot());
        }
    }
}
