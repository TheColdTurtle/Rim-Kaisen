using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugPerformanceChecks
    {
        [DebugAction("Rim-Kaisen", "Stage performance persistence QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StagePersistence()
        {
            var map = Find.CurrentMap; var at = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var caster = JJK_DebugCampaignBalance.Human(map, at, "Performance Save Caster", SorcererGrade.Special, "Deep", "LoveRendezvous");
            var target = JJK_DebugCampaignBalance.Human(map, at + IntVec3.East * 2, "Performance Save Target", SorcererGrade.Special, "Deep", "LoveRendezvous");
            var other = JJK_DebugCampaignBalance.Human(map, at + IntVec3.East * 5, "Performance Save Other", SorcererGrade.Special, "Deep", "LoveRendezvous");
            caster.drafter.Drafted = target.drafter.Drafted = other.drafter.Drafted = true;
            var state = ExpansionTech.State(caster);
            state.nextStar = 0; StarTechnique.Mark(state, target); state.nextStar = 3; StarTechnique.Mark(state, other);
            map.GetComponent<MapComponent_DomainManager>().Open(caster, DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple"));
            map.GetComponent<MapComponent_ExpansionBlasts>().Queue(caster, at, 60000, 0, 0, true);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Log.Message("[JJK Performance Save QA] STAGED marks, domain, delayed blast; save and reload while paused.");
        }

        [DebugAction("Rim-Kaisen", "Verify performance persistence QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void VerifyPersistence()
        {
            var map = Find.CurrentMap;
            var caster = map.mapPawns.AllPawnsSpawned.First(p => p.Name?.ToStringShort == "Performance Save Caster");
            var target = map.mapPawns.AllPawnsSpawned.First(p => p.Name?.ToStringShort == "Performance Save Target");
            var domain = map.GetComponent<MapComponent_DomainManager>().DomainFor(caster);
            bool marks = !StarTechnique.MayMove(target, target.Position + IntVec3.East);
            bool geometry = domain != null && domain.Contains(map, caster.Position)
                && DomainInteriors.CachedCellVisible(domain, map, caster.Position);
            bool blast = map.GetComponent<MapComponent_ExpansionBlasts>().pending.Any(b => b.caster == caster && b.at > ExpansionTech.Now && b.sphere);
            Log.Message("[JJK Performance Save QA] " + (marks && geometry && blast ? "PASS" : "FAIL")
                + " actual save/load: marks=" + marks + " domain=" + geometry + " blast=" + blast);
        }

        [DebugAction("Rim-Kaisen", "Run performance regression checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int passed = 0, failed = 0;
            Action<bool, string> check = (ok, label) =>
            {
                if (ok) passed++; else failed++;
                Log.Message("[JJK Performance QA] " + (ok ? "PASS " : "FAIL ") + label);
            };
            var map = Find.CurrentMap;
            var made = new List<Pawn>();
            var gc = GameComponent_TechniqueExpansion.Instance;
            ActiveDomain domain = null;
            IntVec3 fogCell = IntVec3.Invalid;
            bool originalFog = false;
            try
            {
                check(!JJK_DebugAdvancedPuppetChecks.TryFindClear(null, out var invalid) && !invalid.IsValid, "arena lookup rejects absent map safely");
                if (!JJK_DebugAdvancedPuppetChecks.TryFindClear(map, out var at))
                    throw new InvalidOperationException("Load the prepared arena before running performance regressions.");
                check(at.InBounds(map), "bounded arena lookup finds prepared space");
                Func<string, IntVec3, Pawn> human = (name, cell) =>
                {
                    var p = JJK_DebugCampaignBalance.Human(map, cell, name, SorcererGrade.Special, "Deep", "LoveRendezvous");
                    made.Add(p); return p;
                };
                var caster = human("Performance Caster", at);
                var target = human("Performance Target", at + IntVec3.East * 2);
                var other = human("Performance Other", at + IntVec3.East * 5);
                var state = gc.For(caster);
                check(gc.StarCastersFor(target) == null, "unmarked pawn bypasses star-state scanning");
                state.nextStar = 0; StarTechnique.Mark(state, target);
                state.nextStar = 3; StarTechnique.Mark(state, other);
                var next = target.Position + IntVec3.East;
                check(!StarTechnique.MayMove(target, next), "new marks restrict movement immediately in the same tick");
                state.marks[0].progress = 2;
                check(StarTechnique.MayMove(target, next), "changed route progress takes effect without index rebuild");
                state.marks[0].progress = 0;
                state.marks[1].until = ExpansionTech.Now;
                check(StarTechnique.MayMove(target, next), "expired marks cannot block movement");
                state.marks[1].until = ExpansionTech.Now + 1800;
                var seal = caster.health.AddHediff(JJK_DefOf.JJK_DomainBurnout);
                check(StarTechnique.MayMove(target, next), "live burnout disables indexed caster");
                caster.health.RemoveHediff(seal);
                check(!StarTechnique.MayMove(target, next), "recovery re-enables still-valid marks");
                gc.states.Remove(state);
                check(StarTechnique.MayMove(target, next), "removed state cannot leave stale movement restriction");
                gc.states.Add(state); gc.InvalidateStarTargets();
                StarTechnique.Clear(state);
                check(StarTechnique.MayMove(target, next) && gc.StarCastersFor(target) == null, "clear removes target index immediately");
                state.nextStar = 0; StarTechnique.Mark(state, target);
                state.nextStar = 3; StarTechnique.Mark(state, other);
                gc.InvalidateStarTargets();
                check(!StarTechnique.MayMove(target, next), "transient index reconstructs existing saved marks");
                StarTechnique.Clear(state);

                domain = new ActiveDomain { caster = caster, center = at, sourceAbility = DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Limitless") };
                fogCell = at; originalFog = fogCell.Fogged(map); map.fogGrid.Unfog(fogCell);
                var floor = DomainInteriors.CachedFloor(domain, map);
                check(floor != null && DomainInteriors.CachedCellVisible(domain, map, fogCell), "domain cache builds visible floor");
                check(ReferenceEquals(floor, DomainInteriors.CachedFloor(domain, map)), "unchanged geometry reuses mesh");
                map.fogGrid.Refog(new CellRect(fogCell.x, fogCell.z, 1, 1));
                check(!DomainInteriors.CachedCellVisible(domain, map, fogCell), "paused refog immediately removes cached cell");
                check(!ReferenceEquals(floor, DomainInteriors.CachedFloor(domain, map)), "fog invalidation replaces old mesh");
                map.fogGrid.Unfog(fogCell);
                check(DomainInteriors.CachedCellVisible(domain, map, fogCell), "paused unfog immediately restores cached cell");
                floor = DomainInteriors.CachedFloor(domain, map);
                DomainInteriors.Release(domain);
                check(!ReferenceEquals(floor, DomainInteriors.CachedFloor(domain, map)), "released domain rebuilds without stale mesh");
                DomainInteriors.Release(domain);
                domain.sourceAbility = DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple");
                check(DomainInteriors.CachedFloor(domain, map) == null && DomainInteriors.CachedCellVisible(domain, map, fogCell), "Simple Domain retains boundary without a floor pattern");

                var bosses = map.GetComponent<MapComponent_BossTelegraphs>();
                int count = bosses.Bosses.Count;
                var boss = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Boss_Jogo_Kind"), Faction.OfPlayer);
                made.Add(boss); GenSpawn.Spawn(boss, at + IntVec3.North * 2, map);
                var pattern = boss.TryGetComp<CompBossPattern>();
                check(bosses.Bosses.Count == count + 1 && bosses.Bosses.Contains(pattern), "new boss registers in initialized cache");
                boss.DeSpawn();
                check(bosses.Bosses.Count == count, "despawn removes boss cache entry");
                GenSpawn.Spawn(boss, at + IntVec3.North * 2, map);
                check(bosses.Bosses.Count == count + 1, "respawn registers boss once");
                boss.Destroy();
                check(bosses.Bosses.Count == count, "destruction removes boss cache entry");

                var blasts = new MapComponent_ExpansionBlasts(map);
                blasts.MapComponentTick();
                check(blasts.pending.Count == 0, "empty blast queue is a no-op");
                blasts.Queue(caster, at, 100, 0, 0, true);
                var future = blasts.pending[0];
                blasts.Queue(caster, at, 0, 0, 0, true);
                blasts.Queue(caster, at, 0, 0, 0, true);
                blasts.MapComponentTick();
                check(blasts.pending.Count == 1 && blasts.pending[0] == future, "due blasts removed while future blast retains its fuse");
                future.at = ExpansionTech.Now; blasts.MapComponentTick();
                check(blasts.pending.Count == 0, "future blast resolves exactly when due");

                var curse = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade4_Kind"), Faction.OfPlayer);
                made.Add(curse); GenSpawn.Spawn(curse, at + IntVec3.South * 2, map);
                var teeth = DefDatabase<BodyPartGroupDef>.GetNamed("Teeth");
                var bite = curse.verbTracker.AllVerbs.First(v => v.tool?.linkedBodyPartsGroup == teeth);
                check(curse.meleeVerbs.GetUpdatedAvailableVerbsList(false).Any(v => v.verb == bite), "healthy Grade 4 bite is usable");
                foreach (var part in curse.health.hediffSet.GetNotMissingParts().Where(p => p.groups.Contains(teeth)).ToList())
                    if (!curse.health.hediffSet.PartIsMissing(part)) curse.health.AddHediff(HediffDefOf.MissingBodyPart, part);
                check(!curse.meleeVerbs.GetUpdatedAvailableVerbsList(false).Any(v => v.verb == bite), "lost teeth reproduce unavailable original attack");
                var fallback = curse.meleeVerbs.TryGetMeleeVerb(target);
                check(!curse.Dead && fallback != null && fallback.Available() && fallback.tool?.label == "head", "living toothless Grade 4 curse selects usable fallback");
            }
            catch (Exception ex) { failed++; Log.Error("[JJK Performance QA] EXCEPTION " + ex); }
            finally
            {
                if (domain != null) DomainInteriors.Release(domain);
                if (fogCell.IsValid)
                {
                    if (originalFog) map.fogGrid.Refog(new CellRect(fogCell.x, fogCell.z, 1, 1));
                    else map.fogGrid.Unfog(fogCell);
                }
                foreach (var pawn in made) if (!pawn.Destroyed) pawn.Destroy();
                gc.states.RemoveAll(s => made.Contains(s.pawn)); gc.InvalidateStarTargets();
                Log.Message("[JJK Performance QA] RESULT " + passed + " passed, " + failed + " failed");
            }
        }
    }
}
