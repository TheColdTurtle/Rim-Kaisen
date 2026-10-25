using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Explicit dev-only checks run against the real loaded engine, never on colony pawns.
    public static class JJK_DebugCombatChecks
    {
        private static Ability Learn(Pawn pawn, string name)
        {
            AbilityDef def = DefDatabase<AbilityDef>.GetNamed(name);
            AbilityClass ac = pawn.GetComp<CompAbilities>().GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            foreach (var tree in def.abilityTrees) ac.UnlockTree(tree);
            return ac.GetLearnedAbility(def) ?? ac.LearnAbility(def, false);
        }

        private static Pawn MakeCaster(Map map, IntVec3 cell)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            pawn.GetComp<CompAbilities>().RecheckAbilities();
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        [DebugAction("Rim-Kaisen", "Run combat regression checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            Map map = Find.CurrentMap;
            var created = new List<Pawn>();
            int passed = 0;
            var failures = new List<string>();
            Action<bool, string> check = (condition, name) =>
            {
                if (condition) passed++; else failures.Add(name);
                Log.Message("[JJK QA] " + (condition ? "PASS " : "FAIL ") + name);
            };
            try
            {
                IntVec3 cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
                Pawn caster = MakeCaster(map, cell); created.Add(caster);
                Pawn target = MakeCaster(map, CellFinder.RandomClosewalkCellNear(cell, map, 3)); created.Add(target);
                check(CursedTechUtil.WorkingHandCount(caster) == 2, "healthy caster has two hands");
                BodyPartRecord hand = caster.health.hediffSet.GetNotMissingParts().First(p => p.def == BodyPartDefOf.Hand);
                caster.health.AddHediff(HediffDefOf.MissingBodyPart, hand);
                check(CursedTechUtil.WorkingHandCount(caster) == 1, "missing hand blocks clapping despite surviving arm");
                caster.health.AddHediff(DefDatabase<HediffDef>.GetNamed("BionicArm"), hand.parent);
                check(CursedTechUtil.WorkingHandCount(caster) == 2, "bionic arm supplies a replacement hand");

                float healthy = CursedSpeechUtil.TalkingFactor(caster);
                BodyPartRecord tongue = caster.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Tongue");
                caster.health.AddHediff(HediffDefOf.Cut, tongue).Severity = 4f;
                float hurt = CursedSpeechUtil.TalkingFactor(caster);
                check(hurt < healthy && Mathf.Approximately(hurt, caster.health.capacities.GetLevel(PawnCapacityDefOf.Talking)),
                    "speech tracks actual Talking capacity after tongue injury");
                foreach (var h in caster.health.hediffSet.hediffs.Where(h => h.Part == tongue).ToList()) caster.health.RemoveHediff(h);
                Ability speech = Learn(caster, "JJK_Speech_DontMove");
                var tree = speech.def.abilityTrees[0];
                int xp = GameComponent_JJK.Instance.GetTechniqueXp(caster, tree);
                speech.Cast(null, target);
                Find.Targeter.StopTargeting();
                check(GameComponent_JJK.Instance.GetTechniqueXp(caster, tree) == xp
                    && !caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(h => h.Part == tongue),
                    "cancelled target selection grants no mastery and no tongue wound");
                speech.curTarget = target;
                speech.Start(false);
                check(GameComponent_JJK.Instance.GetTechniqueXp(caster, tree) == xp + 2,
                    "completed speech grants mastery once");
                check(caster.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(h => h.Part == tongue && h.Bleeding),
                    "completed speech leaves a bleeding tongue cut");
                var strain = caster.health.hediffSet.hediffs.OfType<Hediff_CursedSpeechCut>().FirstOrDefault();
                check(strain != null && Mathf.Approximately(strain.Severity, 0.5f)
                    && Mathf.Approximately(strain.BleedRate, 0.01f), "light speech strain is exactly half a hit point with slight bleeding");
                strain?.Tended(1f, 1f);
                check(strain != null && !strain.Bleeding, "tending stops vocal-strain bleeding");

                Ability infinity = Learn(caster, "JJK_Infinity");
                Ability amplification = Learn(caster, "JJK_DomainAmplification");
                infinity.Start(false);
                amplification.Start(false);
                check(!infinity.Active && !InfinityUtil.InfinityActive(caster), "amplification ends Infinity toggle");
                check(DomainAmplificationUtil.Blocks(speech), "amplification blocks innate speech");
                check(!DomainAmplificationUtil.Blocks(Learn(caster, "JJK_ReverseCursedTechnique")),
                    "amplification permits non-innate RCT");
                xp = GameComponent_JJK.Instance.GetTechniqueXp(caster, tree);
                speech.Start(false);
                check(GameComponent_JJK.Instance.GetTechniqueXp(caster, tree) == xp, "blocked direct cast grants no mastery");
                amplification.End();
                check(!DomainAmplificationUtil.Blocks(speech), "ending amplification restores innate casts");
                infinity.Start(false);
                caster.health.AddHediff(JJK_DefOf.JJK_CursedEnergySuppressed);
                check(!InfinityUtil.InfinityActive(caster), "suppression immediately disables Infinity protection");
                infinity.Tick();
                check(!infinity.Active, "suppression ends Infinity upkeep");
                caster.health.RemoveHediff(caster.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_CursedEnergySuppressed));

                caster.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_SoulMender));
                Ability output = Learn(caster, "JJK_PositiveEnergyOutput");
                check(!output.CanHitTarget(target), "positive output rejects ordinary human target");
                Pawn curse = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade2_Kind"));
                created.Add(curse);
                IntVec3 adjacent = GenAdj.CellsAdjacent8Way(caster).First(c => c.InBounds(map) && c.Standable(map));
                GenSpawn.Spawn(curse, adjacent, map);
                check(output.CanHitTarget(curse), "positive output accepts adjacent hostile curse");
                Ability mend = Learn(caster, "JJK_ReverseCursedTechniqueOther");
                check(!mend.CanHitTarget(curse), "positive-energy healing rejects cursed spirit body");
                output.curTarget = curse;
                output.Start(false);
                check(curse.Dead || curse.Destroyed || curse.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(),
                    "positive output damages the cursed spirit in the loaded engine");
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                foreach (Pawn pawn in created) if (!pawn.Destroyed) pawn.Destroy();
            }
            Log.Message($"[JJK QA] RESULT {passed} passed, {failures.Count} failed. " + string.Join(" | ", failures));
            Messages.Message($"Combat checks: {passed} passed, {failures.Count} failed. See log for details.",
                failures.Count == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Rim-Kaisen", "Prepare soul mender practice", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Practice()
        {
            Map map = Find.CurrentMap;
            Pawn pawn = MakeCaster(map, CellFinder.RandomClosewalkCellNear(map.Center, map, 10));
            pawn.Name = new NameSingle("JJK Practice Mender");
            pawn.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_SoulMender));
            Learn(pawn, "JJK_PositiveEnergyOutput");
            Learn(pawn, "JJK_ReverseCursedTechniqueOther");
            pawn.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy = 1000f;
            pawn.drafter.Drafted = true;
            Pawn curse = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade2_Kind"));
            GenSpawn.Spawn(curse, GenAdj.CellsAdjacent8Way(pawn).First(c => c.InBounds(map) && c.Standable(map)), map);
            Find.Selector.ClearSelection(); Find.Selector.Select(pawn);
            Log.Message($"[JJK QA] PRACTICE caster={pawn.ThingID} at {pawn.Position}, curse={curse.ThingID} at {curse.Position}");
        }

        [DebugAction("Rim-Kaisen", "Queue save-load fuse check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void QueueFuse()
        {
            Map map = Find.CurrentMap;
            MapComponent_DelayedBlasts.Queue(map, new IntVec3(10, 0, 10), 240, 1f, DamageDefOf.Bomb, 10, null);
            ReportFuses();
        }

        [DebugAction("Rim-Kaisen", "Report pending fuses", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportFuses() => Log.Message("[JJK QA] Pending fuses="
            + Find.CurrentMap.GetComponent<MapComponent_DelayedBlasts>().PendingCount);

        [DebugAction("Rim-Kaisen", "Report practice combat", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportPractice()
        {
            Map map = Find.CurrentMap;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringFull == "JJK Practice Mender"
                || p.def.HasModExtension<CursedSpiritExtension>()))
            {
                var comp = pawn.GetComp<CompAbilities>();
                var ability = comp?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques)
                    ?.GetLearnedAbility(DefDatabase<AbilityDef>.GetNamed("JJK_PositiveEnergyOutput"));
                Log.Message($"[JJK QA] COMBAT {pawn.ThingID} at {pawn.Position} downed={pawn.Downed} CE={comp?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy)?.energy} lastCast={ability?.lastActivatedTick} job={pawn.CurJobDef?.defName} injuries="
                    + string.Join(",", pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Select(h => $"{h.def.defName}:{h.Severity:0.##}")));
            }
        }
    }
}
