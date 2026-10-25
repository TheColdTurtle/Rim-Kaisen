using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugPresentationChecks
    {
        private static readonly string[] Cues = { "Energy", "Speech", "Nails", "Fire", "Ice", "Healing", "Slash", "Spirit", "Puppet", "DomainOpen", "DomainEnd", "Warning", "Impact" };
        private static Pawn Spawn(Map map, IntVec3 cell, string kind, Faction faction, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind), faction,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            p.Name = new NameSingle(name); GenSpawn.Spawn(p, cell, map); return p;
        }
        [DebugAction("Rim-Kaisen", "Run presentation checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass = 0, fail = 0; var map = Find.CurrentMap;
            var made = new List<Pawn>(); bool sounds = RimKaisenMod.Settings.combatSounds;
            Action<bool,string> check = (ok, name) => { if (ok) pass++; else fail++; Log.Message("[JJK Presentation QA] " + (ok ? "PASS " : "FAIL ") + name); };
            try
            {
                var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
                foreach (string cue in Cues)
                {
                    var def = DefDatabase<SoundDef>.GetNamedSilentFail("JJK_Cue_" + cue);
                    check(def != null && def.subSounds.Count == 1 && def.subSounds[0].resolvedGrains.Count > 0, cue + " sound resolved a real clip");
                }
                foreach (string kind in new[] { "JJK_Mechamaru_Kind", "JJK_AbsolutePuppet_Kind", "JJK_CommunicationPuppet_Kind" })
                {
                    var data = DefDatabase<PawnKindDef>.GetNamed(kind).lifeStages[0].bodyGraphicData;
                    var texture = ContentFinder<Texture2D>.Get(data.texPath + "_south");
                    check(data.Graphic is Graphic_Multi && texture != BaseContent.BadTex && texture.width > 16, kind + " loads dedicated directional sprites");
                }
                var swap = DefDatabase<AbilityDef>.AllDefsListForReading.First(d => d.abilityTrees?.Any(t => t.defName == "JJK_CT_BoogieWoogie") == true);
                check(CombatPresentation.CastCue(swap) == null, "Boogie Woogie remains silent and effect-free");
                check(CombatPresentation.CastCue(DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple")) == null, "domain sound belongs to successful manager opening only");
                RimKaisenMod.Settings.combatSounds = false;
                check(!CombatPresentation.Play("Energy", map, cell), "sound preference disables playback");
                RimKaisenMod.Settings.combatSounds = true;
                check(!CombatPresentation.Play("Energy", map, IntVec3.Invalid), "invalid sound position rejected");
                check(!CombatPresentation.Visible(null, cell), "non-current or absent map cannot render");
                check(CombatPresentation.Countdown(Find.TickManager.TicksGame - 50) == "0.0s", "expired warning never shows negative time");
                var throttle = new MapComponent_CombatAudio(map);
                check(throttle.Accept("A") && !throttle.Accept("A") && throttle.Accept("B"), "same-tick burst suppressed without suppressing different cue");
                var p = Spawn(map, cell, "JJK_Boss_Mahito_Kind", Faction.OfPlayer, "Presentation test"); made.Add(p);
                var pattern = p.TryGetComp<CompBossPattern>(); pattern.aim = cell + IntVec3.East * 8;
                check(pattern.DangerCells().Contains(cell + IntVec3.East) && !pattern.DangerCells().Contains(pattern.aim), "Soul Touch warning follows actual two-tile melee reach, not distant aim");
                var d = new ActiveDomain { sourceAbility = DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple"), integrity = 43.2f };
                check(CombatPresentation.DomainLabel(d).Contains("44"), "Simple Domain label shows current integrity");
                var panda = Spawn(map, cell, "JJK_PandaConstruct_Kind", Faction.OfPlayer, "Presentation panda test"); made.Add(panda);
                Graphic graphic = panda.ageTracker.CurKindLifeStage.bodyGraphicData.Graphic;
                Patch_PandaFormGraphic.Postfix(panda, ref graphic);
                check(graphic is Graphic_Multi && graphic == AnimalCorpseAppearance.GraphicFor(AnimalCorpseAppearance.FormKind(PandaForm.Panda)), "Panda uses actual vanilla animal graphic");
                var core = panda.TryGetComp<CompThreeCores>();
                core.form = PandaForm.Gorilla; Patch_PandaFormGraphic.Postfix(panda, ref graphic);
                check(graphic == AnimalCorpseAppearance.GraphicFor(AnimalCorpseAppearance.FormKind(PandaForm.Gorilla)), "Gorilla uses actual vanilla animal graphic");
                core.form = PandaForm.Sister; Patch_PandaFormGraphic.Postfix(panda, ref graphic);
                check(graphic == AnimalCorpseAppearance.GraphicFor(AnimalCorpseAppearance.Kind("Rhinoceros")), "Sister uses vanilla rhinoceros graphic");
                var doll = Spawn(map, cell, "JJK_CursedCorpseDoll_Kind", Faction.OfPlayer, "Animal shell QA"); made.Add(doll);
                var shell = doll.TryGetComp<CompAnimalCorpseAppearance>(); var charge = doll.TryGetComp<CompCursedCorpse>().charge;
                foreach (var animal in CompAnimalCorpseAppearance.Choices)
                {
                    bool selected = shell.Select(animal); Patch_PandaFormGraphic.Postfix(doll, ref graphic);
                    check(selected && graphic == AnimalCorpseAppearance.GraphicFor(animal) && graphic.MatSouth.mainTexture != BaseContent.BadTex,
                        animal.defName + " shell uses vanilla graphic and texture");
                }
                check(charge == doll.TryGetComp<CompCursedCorpse>().charge, "appearance does not change core charge");
                check(!shell.Select(PawnKindDefOf.Colonist), "non-animal shell selection rejected");
            }
            catch (Exception e) { fail++; Log.Error("[JJK Presentation QA] " + e); }
            finally { foreach (var p in made) if (!p.Destroyed) p.Destroy(); RimKaisenMod.Settings.combatSounds = sounds; }
            Log.Message($"[JJK Presentation QA] RESULT {pass} passed, {fail} failed");
        }

        [DebugAction("Rim-Kaisen", "Prepare presentation QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            var map = Find.CurrentMap; var c = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var caster = Spawn(map, c, "Colonist", Faction.OfPlayer, "Presentation Sorcerer QA");
            caster.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            caster.GetComp<CompAbilities>().RecheckAbilities();
            caster.drafter.Drafted = true;
            PuppetUtility.Energy(caster).energy = 500;
            var manager = map.GetComponent<MapComponent_DomainManager>();
            manager.Open(caster, DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple"));
            string[] kinds = { "JJK_Mechamaru_Kind", "JJK_PandaConstruct_Kind", "JJK_AbsolutePuppet_Kind", "JJK_CursedCorpseDoll_Kind", "JJK_CommunicationPuppet_Kind" };
            for (int i = 0; i < kinds.Length; i++)
            {
                var p = Spawn(map, c + new IntVec3(-8+i*4,0,-5), kinds[i], Faction.OfPlayer, "Presentation " + i + " QA");
                var core = p.TryGetComp<CompCursedCorpse>(); core.controller = caster; core.standby = true;
            }
            MapComponent_DelayedBlasts.Queue(map, c + new IntVec3(7,0,4), 600, 4.5f, DamageDefOf.Bomb, 60, caster);
            Log.Message("[JJK Presentation QA] fixture center=" + c + "; pending=" + map.GetComponent<MapComponent_DelayedBlasts>().PendingCount);
        }
        [DebugAction("Rim-Kaisen", "Prepare boss warning QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BossWarning()
        {
            var map = Find.CurrentMap; var c = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var target = Spawn(map, c, "Colonist", Faction.OfPlayer, "Warning Target QA"); target.drafter.Drafted = true;
            var faction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("JJK_CurseUsers"));
            var boss = Spawn(map, c + IntVec3.East * 6, "JJK_Boss_Jogo_Kind", faction, "Warning Boss QA");
            Log.Message("[JJK Presentation QA] boss begin=" + boss.TryGetComp<CompBossPattern>().Begin(target) + " center=" + c);
        }
        [DebugAction("Rim-Kaisen", "Prepare closed domain QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClosedDomain()
        {
            var map = Find.CurrentMap; var c = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var caster = Spawn(map, c, "Colonist", Faction.OfPlayer, "Domain Presentation QA");
            caster.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            caster.GetComp<CompAbilities>().RecheckAbilities(); caster.drafter.Drafted = true; PuppetUtility.Energy(caster).energy = 500;
            map.GetComponent<MapComponent_DomainManager>().Open(caster, DefDatabase<AbilityDef>.GetNamed("JJK_Domain_BloodManipulation"));
            Log.Message("[JJK Presentation QA] closed domain=" + map.GetComponent<MapComponent_DomainManager>().HasDomain(caster) + " center=" + c);
        }
        [DebugAction("Rim-Kaisen", "Report presentation QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report()
        {
            var map = Find.CurrentMap;
            Log.Message("[JJK Presentation QA] pending count=" + map.GetComponent<MapComponent_DelayedBlasts>().PendingCount);
            foreach (var p in map.GetComponent<MapComponent_DelayedBlasts>().PendingBlasts)
                Log.Message($"[JJK Presentation QA] pending cell={p.cell} radius={p.radius} countdown={CombatPresentation.Countdown(p.triggerTick)} trigger={p.triggerTick}");
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(p => p.LabelShort.Contains("Presentation")))
                Log.Message($"[JJK Presentation QA] pawn={p.LabelShort} cell={p.Position} domain={map.GetComponent<MapComponent_DomainManager>().DomainFor(p)?.integrity} graphic={(p.RaceProps.Humanlike ? "human" : p.ageTracker.CurKindLifeStage.bodyGraphicData?.texPath)}");
        }
        [DebugAction("Rim-Kaisen", "Play presentation sounds QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sounds()
        {
            var map = Find.CurrentMap; var cell = map.mapPawns.FreeColonistsSpawned.First().Position;
            foreach (string cue in Cues) Log.Message("[JJK Presentation QA] playback " + cue + "=" + CombatPresentation.Play(cue, map, cell));
        }
    }
}
