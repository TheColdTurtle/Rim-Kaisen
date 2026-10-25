using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace JJKMod
{
    public static class JJK_DebugIdeologyChecks
    {
        private static T Def<T>(string name) where T : Def => DefDatabase<T>.GetNamed(name);
        private static Pawn Human(Map map, IntVec3 cell, string name, bool sorcerer)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            p.Name = new NameSingle(name);
            foreach (var g in p.genes.GenesListForReading.Where(g => g.def.defName.StartsWith("JJK_")).ToList()) p.genes.RemoveGene(g);
            if (sorcerer) p.genes.AddGene(Def<GeneDef>("JJK_AverageReserves"), true);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }
        [DebugAction("Rim-Kaisen", "Run Jujutsu ideology checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass = 0, fail = 0;
            Action<bool, string> check = (ok, label) => { if (ok) pass++; else fail++; Log.Message("[JJK Ideology QA] " + (ok ? "PASS " : "FAIL ") + label); };
            if (!ModsConfig.IdeologyActive) { check(!DefDatabase<MemeDef>.AllDefs.Any(m => m.defName.StartsWith("JJK_Meme_")), "optional definitions absent without Ideology"); return; }
            var made = new List<Pawn>(); var ideos = new List<Ideo>();
            try
            {
                var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
                var p = Human(map, cell, "Ideology QA", true); made.Add(p);
                var ordinary = Human(map, cell + IntVec3.East, "Ordinary QA", false); made.Add(ordinary);
                var other = Human(map, cell + IntVec3.West, "Sorcerer QA", true); made.Add(other);
                int genes = p.genes.GenesListForReading.Count;
                check(JujutsuIdeology.Sorcerer(p) && !JujutsuIdeology.Sorcerer(ordinary), "active reserves distinguish sorcerers from ordinary people");
                foreach (JujutsuEthic ethic in Enum.GetValues(typeof(JujutsuEthic)))
                {
                    var preset = Def<IdeoPresetDef>("JJK_Preset_" + ethic);
                    var meme = Def<MemeDef>("JJK_Meme_" + ethic);
                    var ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(Faction.OfPlayer.def, forcedMemes: preset.memes,
                        forceNoWeaponPreference: true, name: preset.label));
                    Find.IdeoManager.Add(ideo); ideos.Add(ideo); p.ideo.SetIdeo(ideo);
                    check(ideo.memes.Contains(meme), ethic + " preset generates selected meme");
                    check(ideo.HasPrecept(Def<PreceptDef>("JJK_Ethic_" + ethic)), ethic + " required philosophy precept");
                    check(meme.requireOne.All(group => group.Any(ideo.HasPrecept)), ethic + " all required vanilla precepts");
                    check(ideo.PreceptsListForReading.OfType<Precept_Ritual>().Any(r => r.def.defName == "JJK_Gathering_" + ethic), ethic + " native gathering generated");
                    check(!ideo.memes.Any(m => m != meme && m.GetModExtension<JujutsuEthicExtension>() != null), ethic + " no competing JJK philosophy");
                    check(p.genes.GenesListForReading.Count == genes, ethic + " conversion grants no genes");
                    var memoryComps = Def<PreceptDef>("JJK_Ethic_" + ethic).comps.OfType<PreceptComp_SelfTookMemoryThought>();
                    foreach (var comp in memoryComps)
                    {
                        JujutsuIdeology.Record(p, comp.eventDef.defName.Substring("JJK_Ideo_".Length));
                        JujutsuIdeology.Record(p, comp.eventDef.defName.Substring("JJK_Ideo_".Length));
                        var memories = p.needs.mood.thoughts.memories.Memories.Where(m => m.def == comp.thought).ToList();
                        check(memories.Count == 1 && memories[0].MoodOffset() > 0, ethic + " event produces capped native memory " + comp.eventDef.defName);
                    }
                    if (ethic == JujutsuEthic.Guardian)
                        check(Def<ThoughtDef>("JJK_IdeoMood_Guardian").Worker.CurrentState(p).Active, "guardian values ordinary colony members");
                    if (ethic == JujutsuEthic.Supremacy)
                    {
                        var worker = Def<ThoughtDef>("JJK_IdeoRespect_Supremacy").Worker;
                        check(worker.CurrentSocialState(p, ordinary).StageIndex == 0, "supremacist prejudice targets ordinary people");
                        check(worker.CurrentSocialState(p, other).StageIndex == 1, "supremacist respect recognizes sorcerer of another ideology");
                        check(Def<ThoughtDef>("JJK_IdeoMood_Supremacy").Worker.CurrentState(p).StageIndex == 0, "mixed colony creates supremacist tension");
                    }
                    if (ethic == JujutsuEthic.Clan)
                    {
                        var role = ideo.PreceptsListForReading.OfType<Precept_Role>().First(r => r.def.leaderRole);
                        role.Assign(p, true); GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Grade4);
                        var worker = Def<ThoughtDef>("JJK_IdeoMood_Clan").Worker;
                        check(worker.CurrentState(p).StageIndex == 0, "clan accepts assignment but disapproves of junior leader");
                        GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Grade1);
                        check(worker.CurrentState(p).StageIndex == 1, "clan recognizes promoted leader");
                        role.Unassign(p, false);
                    }
                }
                // Validate actual action hooks rather than only calling the history adapter.
                p.ideo.SetIdeo(ideos[(int)JujutsuEthic.Guardian]);
                var exorcism = Def<ThoughtDef>("JJK_IdeoMemory_Guardian_Exorcism");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(exorcism);
                var curse = PawnGenerator.GeneratePawn(Def<PawnKindDef>("JJK_Curse_Grade4_Kind"));
                made.Add(curse); GenSpawn.Spawn(curse, cell + IntVec3.North * 3, map);
                curse.Kill(new DamageInfo(DamageDefOf.Cut, 1000, 1, -1, p));
                check(p.needs.mood.thoughts.memories.Memories.Any(m => m.def == exorcism), "actual hostile curse Kill grants exorcism memory");
                p.ideo.SetIdeo(ideos[(int)JujutsuEthic.Reform]);
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(exorcism);
                JujutsuIdeology.Record(p, "Exorcism");
                check(!p.needs.mood.thoughts.memories.Memories.Any(m => m.def == exorcism), "conversion stops old philosophy event rewards");
                check(JujutsuIdeology.Follows(p, JujutsuEthic.Reform) && !JujutsuIdeology.Follows(p, JujutsuEthic.Guardian), "conversion updates belief classification");
                other.ideo.SetIdeo(p.Ideo);
                var mentorMemory = Def<ThoughtDef>("JJK_IdeoMemory_Reform_Mentorship");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(mentorMemory);
                other.needs.mood.thoughts.memories.RemoveMemoriesOfDef(mentorMemory);
                p.genes.AddGene(Def<GeneDef>("JJK_CT_Gene_CursedSpeech"), true);
                var tree = Def<TaranMagicFramework.AbilityTreeDef>("JJK_CT_CursedSpeech");
                JujutsuTraining.Class(p).UnlockTree(tree); ExpansionTech.Energy(p).energy = 60;
                var plan = new TrainingPlan { pupil = p, mentor = other, tree = tree, enabled = false };
                check(JujutsuTraining.Complete(plan) == 0 && !p.needs.mood.thoughts.memories.Memories.Any(m => m.def == mentorMemory), "failed practice grants no mentorship reward");
                plan.enabled = true;
                check(JujutsuTraining.Complete(plan) > 0 && p.needs.mood.thoughts.memories.Memories.Any(m => m.def == mentorMemory)
                    && other.needs.mood.thoughts.memories.Memories.Any(m => m.def == mentorMemory), "actual completed practice rewards pupil and mentor");
                p.ideo.SetIdeo(ideos[(int)JujutsuEthic.Evolution]);
                var discovery = Def<ThoughtDef>("JJK_IdeoMemory_Evolution_Discovery");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(discovery);
                var project = DefDatabase<ResearchProjectDef>.AllDefs.First(r => r.defName.StartsWith("JJK_") && !r.IsFinished);
                Find.ResearchManager.FinishProject(project, false, p);
                check(project.IsFinished && p.needs.mood.thoughts.memories.Memories.Any(m => m.def == discovery), "actual Jujutsu research completion rewards discovery");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(discovery);
                Find.ResearchManager.FinishProject(project, false, p);
                check(!p.needs.mood.thoughts.memories.Memories.Any(m => m.def == discovery), "already finished research cannot farm discovery");
                p.ideo.SetIdeo(ideos[(int)JujutsuEthic.Sovereign]);
                var victory = Def<ThoughtDef>("JJK_IdeoMemory_Sovereign_Victory");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(victory);
                var strongCurse = PawnGenerator.GeneratePawn(Def<PawnKindDef>("JJK_Curse_Grade1_Kind"));
                made.Add(strongCurse); GenSpawn.Spawn(strongCurse, cell + IntVec3.North * 4, map);
                strongCurse.Kill(new DamageInfo(DamageDefOf.Cut, 1000, 1, -1, p));
                check(p.needs.mood.thoughts.memories.Memories.Any(m => m.def == victory), "standing strong hostile grants worthy victory");
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(victory);
                var fallen = PawnGenerator.GeneratePawn(Def<PawnKindDef>("JJK_Curse_Grade1_Kind"));
                made.Add(fallen); GenSpawn.Spawn(fallen, cell + IntVec3.North * 5, map);
                fallen.health.AddHediff(HediffDefOf.Anesthetic);
                check(fallen.Downed, "unconscious hostile fixture is downed");
                fallen.Kill(new DamageInfo(DamageDefOf.Cut, 1000, 1, -1, p));
                check(!p.needs.mood.thoughts.memories.Memories.Any(m => m.def == victory), "downed victims do not grant worthy victory");
                ordinary.genes.AddGene(Def<GeneDef>("JJK_PhysicalPerfection"), true);
                check(!JujutsuIdeology.Sorcerer(ordinary) && JujutsuIdeology.Strong(ordinary), "zero-energy restriction is respected for strength but excluded by supremacists");
                Log.Message("[JJK Ideology QA] Ideologies: " + string.Join(" | ", ideos.Select(i => i.name + ": " + i.description)));
            }
            catch (Exception e) { fail++; Log.Message("[JJK Ideology QA] EXCEPTION " + e); }
            finally
            {
                foreach (var p in made) if (!p.Destroyed) p.Destroy();
                Faction.OfPlayer.ideos.RecalculateIdeosBasedOnPlayerPawns();
                foreach (var i in ideos) Find.IdeoManager.Remove(i);
                Log.Message("[JJK Ideology QA] RESULT " + pass + " passed, " + fail + " failed");
            }
        }

        [DebugAction("Rim-Kaisen", "Preview Jujutsu ideologies", false, false, true, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Preview()
        {
            var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
            Ideo shown = null;
            foreach (JujutsuEthic ethic in Enum.GetValues(typeof(JujutsuEthic)))
            {
                var preset = Def<IdeoPresetDef>("JJK_Preset_" + ethic);
                var ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(Faction.OfPlayer.def, forcedMemes: preset.memes, forceNoWeaponPreference: true));
                ideo.name = preset.label; Find.IdeoManager.Add(ideo);
                var p = Human(map, cell + IntVec3.East * (int)ethic, ethic + " follower QA", true);
                p.ideo.SetIdeo(ideo);
                if (ethic == JujutsuEthic.Clan)
                {
                    GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Grade1);
                    ideo.PreceptsListForReading.OfType<Precept_Role>().First(r => r.def.leaderRole).Assign(p, true);
                }
                if (ethic == JujutsuEthic.Guardian) { shown = ideo; JujutsuIdeology.Record(p, "Service"); }
            }
            Find.WindowStack.Add(new Dialog_Ideo(shown));
            Log.Message("[JJK Ideology QA] Six preview followers created for UI and save/load checks; use only in a disposable QA save.");
        }

        [DebugAction("Rim-Kaisen", "Preview ideology preset page", false, false, true, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PreviewPresets() => Find.WindowStack.Add(new Page_ChooseIdeoPreset());
    }
}
