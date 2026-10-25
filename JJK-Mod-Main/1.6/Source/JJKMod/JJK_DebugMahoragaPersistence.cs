using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace JJKMod
{
    public static partial class JJK_DebugMahoragaChecks
    {
        private static Pawn SavedWheel => Find.CurrentMap.mapPawns.AllPawnsSpawned.First(p => p.Name?.ToStringFull == "Mahoraga persistence wheel");
        private static Pawn SavedCaster => Find.CurrentMap.mapPawns.AllPawnsSpawned.First(p => p.Name?.ToStringFull == "Mahoraga persistence caster");
        private static List<Thing> Payloads => Find.CurrentMap.listerThings.AllThings.Where(t => MahoragaDelivery.Get(t) != null
            && ((t as Projectile)?.Launcher == SavedCaster || (t as Explosion)?.instigator == SavedCaster)).OrderBy(t => t.ThingID).ToList();
        private static string PersistenceSnapshot()
        {
            Pawn p = SavedWheel; var c = p.TryGetComp<Comp_MahoragaAdaptation>();
            string wheel = string.Join(";", new[] { "JJK_Adapt_Ballistic", "JJK_Adapt_Blast", "JJK_Adapt_Shrine", "JJK_Adapt_Void", "JJK_Adapt_Purple" }
                .Select(key => key + ":" + c.GetLevel(key) + ":" + (c.PendingTicks(key) > 0 ? c.PendingTicks(key) + Find.TickManager.TicksGame : 0)));
            return p.ThingID + "|" + wheel + "|" + p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity)
                + "|" + string.Join(";", Payloads.Select(t => t.ThingID + ":" + MahoragaDelivery.Get(t)))
                + "|" + string.Join(";", Find.CurrentMap.GetComponent<MapComponent_DelayedBlasts>().PendingBlasts.Where(b => b.instigator == SavedCaster).Select(b => b.phenomenon + ":" + b.triggerTick))
                + "|" + string.Join(";", Find.CurrentMap.GetComponent<MapComponent_ExpansionBlasts>().pending.Where(b => b.caster == SavedCaster).Select(b => b.phenomenon + ":" + b.at));
        }

        [DebugAction("Rim-Kaisen", "Stage Mahoraga persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StagePersistence()
        {
            Find.TickManager.Pause(); var map = Find.CurrentMap; var at = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var caster = JJK_DebugCampaignBalance.Human(map, at, "Mahoraga persistence caster", SorcererGrade.Calamity, "Bottomless", "Limitless");
            var p = Make(map, at); p.Name = new NameSingle("Mahoraga persistence wheel"); p.Position = at + IntVec3.East * 6;
            var c = p.TryGetComp<Comp_MahoragaAdaptation>();
            for (int i = 0; i < 3; i++) c.Adapt(DamageDefOf.Bullet);
            c.Adapt(DamageDefOf.Bomb); c.Adapt("JJK_Adapt_Shrine");
            using (new MahoragaExposure(MahoragaPhenomena.Void, caster)) p.stances.stunner.StunFor(1800, caster, false, true);
            Turns(c, 113);
            using (new MahoragaExposure("JJK_Adapt_Purple", caster))
            {
                Hurt(p, DamageDefOf.Bomb, caster, 3);
                var projectile = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Bullet_AssaultRifle"), caster.Position, map);
                projectile.Launch(caster, p, p, ProjectileHitFlags.IntendedTarget);
                // Keep the test shot in flight through RimWorld's first frame after loading.
                projectile.ticksToImpact = 10000; projectile.lifetime = 10000;
            }
            using (new MahoragaExposure("JJK_Adapt_Meteor", caster)) MapComponent_DelayedBlasts.Queue(map, p.Position, 900, 1, DamageDefOf.Bomb, 5, caster);
            using (new MahoragaExposure("JJK_Adapt_Sphere", caster)) map.GetComponent<MapComponent_ExpansionBlasts>().Queue(caster, p.Position, 900, 1, 5, true);
            using (new MahoragaExposure("JJK_Adapt_Red", caster)) GenExplosion.DoExplosion(p.Position, map, 1f, DamageDefOf.Bomb, caster, 5, 100);
            foreach (var explosion in Payloads.OfType<Explosion>()) explosion.startTick = Find.TickManager.TicksGame + 5000;
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "persistence-expected.txt"), PersistenceSnapshot());
            File.WriteAllText(Path.Combine(Folder, "persistence-staged-tick.txt"), Find.TickManager.TicksGame.ToString());
            Find.Selector.ClearSelection(); Find.Selector.Select(p); CameraJumper.TryJump(p);
            Log.Message("[JJK Mahoraga QA] Persistence staged: " + PersistenceSnapshot());
        }

        [DebugAction("Rim-Kaisen", "Verify Mahoraga persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void VerifyPersistence()
        {
            var log = new List<string>();
            Action<bool, string> check = (ok, name) => { var s = (ok ? "PASS " : "FAIL ") + name; log.Add(s); Log.Message("[JJK Mahoraga QA] " + s); };
            try
            {
                check(File.ReadAllText(Path.Combine(Folder, "persistence-expected.txt")) == PersistenceSnapshot(), "actual save/load preserves levels, timers, injuries and all delivery identities");
                var p = SavedWheel; var caster = SavedCaster; var c = p.TryGetComp<Comp_MahoragaAdaptation>(); var map = p.Map;
                var projectile = Payloads.OfType<Projectile>().First(t => t.Launcher == caster);
                float before = p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
                float oldPenetration = projectile.def.projectile.armorPenetrationBase;
                try
                {
                    // Ordinary armor must not randomly mask whether adaptation absorbed this shot.
                    projectile.def.projectile.armorPenetrationBase = 100;
                    AccessTools.Method(typeof(Bullet), "Impact").Invoke(projectile, new object[] { p, false });
                }
                finally { projectile.def.projectile.armorPenetrationBase = oldPenetration; }
                check(p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) > before, "reloaded tagged bullet bypasses conventional bullet immunity");
                var explosion = Payloads.OfType<Explosion>().First(t => t.instigator == caster);
                explosion.startTick = Find.TickManager.TicksGame - 1000; explosion.Tick();
                check(c.PendingTicks("JJK_Adapt_Red") > 0, "reloaded expanding blast teaches Red rather than ordinary explosions");
                var delayed = map.GetComponent<MapComponent_DelayedBlasts>();
                foreach (var b in delayed.PendingBlasts.Where(b => b.instigator == caster)) b.triggerTick = Find.TickManager.TicksGame;
                delayed.MapComponentTick();
                var meteor = Payloads.OfType<Explosion>().First(t => t.instigator == caster && MahoragaDelivery.Get(t) == "JJK_Adapt_Meteor");
                meteor.startTick = Find.TickManager.TicksGame - 1000; meteor.Tick();
                check(c.PendingTicks("JJK_Adapt_Meteor") > 0, "reloaded delayed Meteor retains identity at impact");
                var expansion = map.GetComponent<MapComponent_ExpansionBlasts>();
                foreach (var b in expansion.pending.Where(b => b.caster == caster)) b.at = Find.TickManager.TicksGame;
                expansion.MapComponentTick();
                check(c.PendingTicks("JJK_Adapt_Sphere") > 0, "reloaded Perfect Sphere retains identity at impact");
                check(c.PendingTicks(MahoragaPhenomena.Void) + Find.TickManager.TicksGame == int.Parse(File.ReadAllText(Path.Combine(Folder, "persistence-staged-tick.txt"))) + 307, "pending Void adaptation retains exact completion time");
                Turns(c, c.PendingTicks(MahoragaPhenomena.Void)); check(c.GetLevel(MahoragaPhenomena.Void) == 1, "loaded wheel completes at remaining duration");
                check(!p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "loaded spin fully heals projectile and blast wounds");
                check(MahoragaExposure.Current == null, "delivery scopes restored after reloaded impacts");
            }
            catch (Exception ex) { log.Add("FAIL " + ex); Log.Error("[JJK Mahoraga QA] " + ex); }
            Directory.CreateDirectory(Folder); File.WriteAllLines(Path.Combine(Folder, "persistence-results.txt"), log);
        }
    }
}
