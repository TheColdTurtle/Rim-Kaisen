using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public static class JJK_DebugProgressionChecks
    {
        [DebugAction("Rim-Kaisen", "Run progression regression checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            var pawns = new List<Pawn>();
            int passed = 0;
            var failed = new List<string>();
            Action<bool, string> check = (ok, name) =>
            {
                if (ok) passed++; else failed.Add(name);
                Log.Message("[JJK Progression QA] " + (ok ? "PASS " : "FAIL ") + name);
            };
            try
            {
                Pawn pawn = MakePawn(Faction.OfPlayer); pawns.Add(pawn);
                Pawn enemy = MakePawn(Faction.OfAncientsHostile); pawns.Add(enemy);
                pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
                pawn.GetComp<CompAbilities>().RecheckAbilities();
                var hostileHit = new DamageInfo(DamageDefOf.Cut, 1f, instigator: enemy);
                check(RCTAwakeningUtil.CanAwaken(pawn), "ordinary sorcerer is eligible to learn RCT");
                check(!RCTAwakeningUtil.TryMark(pawn, hostileHit), "healthy pawn cannot gain near-death insight");

                Injure(pawn);
                check(pawn.Downed && !pawn.Dead && pawn.health.summaryHealth.SummaryHealthPercent <= 0.4f,
                    "fixture is alive and downed with severe distributed injuries");
                check(!RCTAwakeningUtil.TryMark(pawn, null), "non-damage downing is excluded");
                check(!RCTAwakeningUtil.TryMark(pawn, new DamageInfo(DamageDefOf.Cut, 1f, instigator: pawn)),
                    "self-inflicted downing is excluded");
                pawn.health.MakeUndowned(null);
                pawn.health.MakeDowned(hostileHit, null);
                check(pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_NearDeathInsight")),
                    "hostile downing invokes the patched method and creates insight");
                check(!RCTAwakeningUtil.TryMark(pawn, hostileHit), "repeat downing cannot reset the opportunity");
                Hediff insight = pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_NearDeathInsight"));
                var comp = insight.TryGetComp<HediffComp_RCTAwakening>();
                AccessTools.Field(typeof(HediffComp_RCTAwakening), "roll").SetValue(comp, 0f);
                comp.ResolveRecovery();
                check(!comp.Resolved && !RCTAwakeningUtil.HasRCT(pawn), "RCT is not awarded while still downed");
                foreach (var injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                    pawn.health.RemoveHediff(injury);
                pawn.health.Notify_HediffChanged(null);
                comp.ResolveRecovery();
                check(comp.Resolved && RCTAwakeningUtil.HasRCT(pawn), "successful saved roll awakens RCT after recovery");
                check(!RCTAwakeningUtil.Awaken(pawn), "awakening cannot duplicate an existing healing trait");
                var ac = pawn.GetComp<CompAbilities>().GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
                check(ac.UnlockedTrees.Contains(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_RCT")),
                    "awakening makes the RCT tree available immediately");

                MenderInsightUtil.Awaken(pawn);
                var requirement = new AcquireRequirement { requiredTraits = new List<TraitDef> { JJK_DefOf.JJK_RCT } };
                check(requirement.RequirementSatisfied(ac, null), "soul mender satisfies an RCT prerequisite");
                requirement.requiredGenes = new List<GeneDef> { DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Limitless") };
                // Deliberately remove a random Limitless roll if the fixture received one.
                foreach (var gene in pawn.genes.GenesListForReading.Where(g => g.def == requirement.requiredGenes[0]).ToList())
                    pawn.genes.RemoveGene(gene);
                check(!requirement.RequirementSatisfied(ac, null), "RCT substitute still enforces missing Limitless gene");

                check(!HediffComp_BindingVowConditional.IsOvertimeHour(8)
                    && !HediffComp_BindingVowConditional.IsOvertimeHour(17)
                    && HediffComp_BindingVowConditional.IsOvertimeHour(18)
                    && HediffComp_BindingVowConditional.IsOvertimeHour(7), "Overtime switches at 08:00 and 18:00");
                BindingVowDef vow = DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Overtime");
                BindingVowUtil.ApplyVow(pawn, vow);
                check(BindingVowUtil.HasAnyVow(pawn), "Overtime occupies the existing single-vow slot");
                Hediff vowHediff = pawn.health.hediffSet.GetFirstHediffOfDef(vow.grantHediffs[0]);
                bool overtime = HediffComp_BindingVowConditional.IsOvertimeHour(GenLocalDate.HourInteger(pawn));
                check(Mathf.Approximately(vowHediff.CurStage.statFactors.Single(s => s.stat == JJK_DefOf.JJK_CTOutput).value,
                    overtime ? 1.2f : 0.8f), "vow immediately applies the correct local-time output factor");

                var bench = DefDatabase<ThingDef>.GetNamed("JJK_CursedMaterialsBench");
                foreach (string name in new[] { "NewShadowStyle", "SimpleDomain", "FallingBlossomEmotion", "DomainAmplification" })
                {
                    RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed("JJK_Write" + name + "Scroll");
                    check(bench.AllRecipes.Contains(recipe) && recipe.ingredients.Count == 2
                        && recipe.researchPrerequisites.Any(r => r.defName == "JJK_ScrollTranscription")
                        && recipe.products.Count == 1 && recipe.products[0].count == 1,
                        name + " scroll recipe loads with research, materials and one product");
                    Thing scroll = ThingMaker.MakeThing(recipe.products[0].thingDef);
                    scroll.TryGetComp<CompLearnAbilityScroll>().DoEffect(pawn);
                    check(scroll.Destroyed && ac.GetLearnedAbility(recipe.products[0].thingDef
                        .GetCompProperties<CompProperties_LearnAbilityScroll>().abilityToLearn) != null,
                        name + " scroll is consumed and teaches the existing ability");
                }
            }
            catch (Exception ex) { failed.Add(ex.ToString()); }
            finally { foreach (Pawn pawn in pawns) if (!pawn.Destroyed) pawn.Destroy(); }
            Log.Message($"[JJK Progression QA] RESULT {passed} passed, {failed.Count} failed. " + string.Join(" | ", failed));
        }

        private static Pawn MakePawn(Faction faction)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (Hediff h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            foreach (Trait trait in pawn.story.traits.allTraits.Where(t => t.def == JJK_DefOf.JJK_RCT
                || t.def == JJK_DefOf.JJK_SoulMender).ToList()) pawn.story.traits.RemoveTrait(trait);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(JJK_DebugAdvancedPuppetChecks.Clear(Find.CurrentMap), Find.CurrentMap, 3), Find.CurrentMap);
            return pawn;
        }

        private static void Injure(Pawn pawn)
        {
            // Stay below vanilla's lethal total-injury limit; never injure conceptual parts.
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.defName == "Torso" || p.def.defName == "Arm" || p.def.defName == "Leg").ToList())
            {
                Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
                injury.Severity = part.def.GetMaxHealth(pawn) * 0.7f;
                pawn.health.AddHediff(injury);
                injury.Tended(1f, 1f);
            }
            pawn.health.Notify_HediffChanged(null);
            Log.Message($"[JJK Progression QA] Fixture: downed={pawn.Downed} dead={pawn.Dead} health={pawn.health.summaryHealth.SummaryHealthPercent}");
        }

        [DebugAction("Rim-Kaisen", "Prepare RCT persistence check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PreparePersistence()
        {
            Pawn pawn = MakePawn(Faction.OfPlayer);
            pawn.Name = new NameSingle("RCT Persistence QA");
            pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            pawn.GetComp<CompAbilities>().RecheckAbilities();
            Injure(pawn);
            Pawn enemy = MakePawn(Faction.OfAncientsHostile);
            try { RCTAwakeningUtil.TryMark(pawn, new DamageInfo(DamageDefOf.Cut, 1f, instigator: enemy)); }
            finally { enemy.Destroy(); }
            ReportPersistence();
        }

        [DebugAction("Rim-Kaisen", "Report RCT persistence check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportPersistence()
        {
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringShort == "RCT Persistence QA"))
            {
                var comp = pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_NearDeathInsight"))
                    ?.TryGetComp<HediffComp_RCTAwakening>();
                Log.Message(comp == null ? "[JJK Progression QA] Persistence marker MISSING" :
                    $"[JJK Progression QA] Persistence {pawn.ThingID}: downed={pawn.Downed} resolved={comp.Resolved} roll={AccessTools.Field(typeof(HediffComp_RCTAwakening), "roll").GetValue(comp)} chance={AccessTools.Field(typeof(HediffComp_RCTAwakening), "chance").GetValue(comp)}");
            }
        }
    }
}
