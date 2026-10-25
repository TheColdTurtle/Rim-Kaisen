using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_DebugStudyEncounterChecks
    {
        private static Pawn Human(Map m, IntVec3 c, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn:true, canGeneratePawnRelations:false));
            p.Name = new NameSingle(name);
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_CursedSpeech"), true);
            p.GetComp<CompAbilities>().RecheckAbilities();
            GenSpawn.Spawn(p,c,m); GameComponent_JJK.Instance.SetGrade(p,SorcererGrade.Grade4); return p;
        }
        private static Thing Item(string def, Map m, IntVec3 c)
        { var t = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def)); GenSpawn.Spawn(t,c,m); if(t.TryGetComp<CompForbiddable>()!=null)t.SetForbidden(false); return t; }
        private static Pawn Curse(Map m, IntVec3 c)
        { var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade4_Kind")); GenSpawn.Spawn(p,c,m); return p; }
        [DebugAction("Rim-Kaisen", "Run study and encounter checks", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass=0,fail=0; var m=Find.CurrentMap; var made=new List<Thing>();
            var gc=GameComponent_Jujutsu.Instance; var oldContracts=gc.contracts.ToList(); var oldStanding=gc.standings.ToList();
            var encounters=m.GetComponent<MapComponent_CurseEncounters>(); var oldEncounter=encounters.active; int oldNext=encounters.nextIncident;
            float factor=RimKaisenMod.Settings.gradeXpFactor;
            Action<bool,string> check=(ok,name)=> { if(ok)pass++;else fail++; Log.Message("[JJK Study QA] "+(ok?"PASS ":"FAIL ")+name); };
            try
            {
                RimKaisenMod.Settings.gradeXpFactor=1;
                var c=JJK_DebugAdvancedPuppetChecks.Clear(m); var p=Human(m,c,"Study test"); made.Add(p);
                var ally=Human(m,c+IntVec3.East*3,"Study ally"); made.Add(ally);
                var progress=GameComponent_JJK.Instance; var r=GameComponent_SorcererStudy.Instance.For(p);
                var tree=DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech"); JujutsuTraining.Class(p).UnlockTree(tree);
                check(StudyManuals.OwnedTrees(p).Contains(tree),"active innate technique appears in mastery choices");
                var book=Item("JJK_GradeTrainingManual",m,c+IntVec3.East); made.Add(book);
                check(StudyManuals.Complete(p,book,null) && book.Destroyed && progress.GetGradeXp(p)==75,"grade manual consumes once and grants 75 XP");
                check(StudyManuals.Reason(p,false)!=null,"shared study cooldown enforced");
                r.nextStudy=0; check(StudyManuals.Reason(p,false)!=null,"same-grade second manual blocked");
                progress.SetGrade(p,SorcererGrade.Grade3); check(StudyManuals.Reason(p,false)==null && StudyManuals.GradeGain(p)==100,"new grade permits one new manual");
                progress.SetGrade(p,SorcererGrade.Special); check(StudyManuals.Reason(p,false)!=null,"special grade cannot use grade manual");
                progress.SetGrade(p,SorcererGrade.Grade4);
                int genes=p.genes.GenesListForReading.Count; int masteryBefore=progress.GetTechniqueXp(p,tree);
                foreach(int gain in new[]{250,125,60})
                {
                    r.nextStudy=0; book=Item("JJK_TechniqueMasteryManual",m,c+IntVec3.East); made.Add(book);
                    check(StudyManuals.MasteryGain(p,tree)==gain && StudyManuals.Complete(p,book,tree),"mastery manual grants diminishing "+gain);
                }
                r.nextStudy=0;
                check(progress.GetTechniqueXp(p,tree)==masteryBefore+435 && StudyManuals.MasteryGain(p,tree)==0,"mastery total capped at 435 per technique");
                check(p.genes.GenesListForReading.Count==genes,"manuals never grant or reroll genes");
                book=Item("JJK_TechniqueMasteryManual",m,c+IntVec3.East); made.Add(book);
                check(!StudyManuals.Complete(p,book,DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Limitless")) && !book.Destroyed,"unowned technique rejected without consuming item");
                r.masteryBooks.Clear(); p.drafter.Drafted=true;
                check(StudyManuals.Reason(p,true,tree)!=null,"drafted study rejected"); p.drafter.Drafted=false;
                StudyManuals.Start(p,book,tree);
                check(p.CurJob?.def.defName=="JJK_StudyManual" && ((JobDriver_StudyManual)p.jobs.curDriver).tree==tree,"real job carries chosen mastery tree");
                p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                check(!book.Destroyed && progress.GetTechniqueXp(p,tree)==masteryBefore+435,"cancelled job consumes neither item nor use");

                gc.contracts.Clear(); gc.standings=new List<SchoolStanding>();
                var school=GameComponent_Jujutsu.Schools.First(f=>!f.HostileTo(Faction.OfPlayer));
                var standing=gc.Standing(school); standing.reputation=50;
                check(PromotionTrials.Reason(school,p)==null,"eligible school trial available");
                var trial=gc.Accept(school,m,PromotionTrials.Tier(p),ContractKind.Infestation,p);
                check(trial!=null && trial.promotionTrial && trial.threats.Count>0,"trial creates actual grade-scaled curse pack");
                made.AddRange(trial.threats);
                check(PromotionTrials.Reason(school,p)!=null,"duplicate candidate trial blocked");
                foreach(var victim in trial.threats)
                {
                    victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt,1,0,-1,p));
                    victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt,1,0,-1,p));
                }
                check(trial.trialEngaged.Count==trial.threats.Count,"actual damage hook records distinct enemies without double counting");
                foreach(var victim in trial.threats.ToList()) if(!victim.Destroyed) victim.Destroy();
                int xp=progress.GetGradeXp(p), allyXp=progress.GetGradeXp(ally);
                int count=m.listerThings.ThingsOfDef(trial.rewardDef).Sum(t=>t.stackCount);
                check(gc.Claim(trial) && trial.state==ContractState.Completed,"engaged cleared trial pays reward");
                check(progress.GetGradeXp(p)==xp+50 && progress.GetGradeXp(ally)==allyXp,"trial XP belongs only to candidate");
                check(m.listerThings.ThingsOfDef(trial.rewardDef).Sum(t=>t.stackCount)==count+1,"tier-one trial pays exactly one weapon");
                check(standing.reputation==60 && r.trialPasses==1 && r.nextTrial>Find.TickManager.TicksGame,"standing pass record and five-day cooldown awarded");
                check(!gc.Claim(trial),"trial cannot pay twice");
                r.trialPasses=0; r.nextTrial=0; standing.nextOfferTick=0;
                var failed=gc.Accept(school,m,1,ContractKind.Infestation,p); made.AddRange(failed.threats);
                foreach(var victim in failed.threats.ToList()) if(!victim.Destroyed)victim.Destroy();
                gc.Evaluate(failed); check(failed.state==ContractState.Failed && !gc.Claim(failed),"allied-only clear without candidate participation fails");
                for(int i=0;i<4;i++) { standing.reputation=i*10; check(!gc.RewardFor(school,3).defName.StartsWith("JJK_Remnant"),"tier-three reward "+i+" does not guarantee innate gene"); }

                var nexus=Item("JJK_CursedNexus",m,c+IntVec3.North*2); made.Add(nexus);
                var curse=Curse(m,c+IntVec3.North); made.Add(curse);
                var doll=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_CursedCorpseDoll_Kind"),Faction.OfPlayer);
                GenSpawn.Spawn(doll,c+IntVec3.West,m); made.Add(doll);
                check(MapComponent_CurseEncounters.MiasmaAffects(p,nexus),"living sorcerer exposed inside field");
                check(!MapComponent_CurseEncounters.MiasmaAffects(curse,nexus) && !MapComponent_CurseEncounters.MiasmaAffects(doll,nexus),"curses and constructed corpses immune");
                ally.Position=c+IntVec3.East*9;
                check(!MapComponent_CurseEncounters.MiasmaAffects(ally,nexus),"outside radius protected");
                var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel); GenSpawn.Spawn(wall,c+IntVec3.North,m); made.Add(wall);
                check(!MapComponent_CurseEncounters.MiasmaAffects(p,nexus),"solid wall blocks miasma"); wall.Destroy();
                var hazard=new CurseEncounter {kind=CurseEncounterKind.Miasma,nexus=nexus,center=nexus.Position,manifested=true,endsAt=Find.TickManager.TicksGame+60000};
                encounters.Pulse(hazard);
                check(p.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_CursedMiasmaExposure")),"actual hazard pulse applies exposure");
                nexus.Destroy(); encounters.active=hazard; encounters.MapComponentTick();
                check(encounters.active==null,"destroyed source ends encounter");
                hazard.nexus=null; encounters.active=hazard; encounters.MapComponentTick();
                check(encounters.active==null,"missing saved manifested source cannot respawn");
                encounters.active=null; encounters.nextIncident=0;
                check(encounters.Begin(CurseEncounterKind.Ambush,200) && encounters.active.triggerAt==Find.TickManager.TicksGame+600,"ambush schedules ten-second warning");
                check(!encounters.Begin(CurseEncounterKind.Miasma,200),"one encounter at a time");
                var before=m.mapPawns.AllPawnsSpawned.ToList(); encounters.active.triggerAt=Find.TickManager.TicksGame; encounters.MapComponentTick();
                var spawned=m.mapPawns.AllPawnsSpawned.Except(before).ToList(); made.AddRange(spawned);
                check(encounters.active==null && spawned.Count>0 && spawned.All(JJK_Utils.IsCursedSpirit),"warning resolves into actual curse pack");
                check(!encounters.Available,"two-day encounter cooldown remains after attack");
                encounters.nextIncident=0;
                check(encounters.Begin(CurseEncounterKind.Stalker,200),"stalker incident starts");
                var stalking=encounters.active; made.Add(stalking.stalker);
                check(!stalking.stalker.InMentalState && stalking.triggerAt==Find.TickManager.TicksGame+1800,"stalker begins neutral with thirty-second preparation");
                stalking.target=p; ally.Position=c+IntVec3.East;
                var escort=Human(m,c+IntVec3.West,"Second escort"); made.Add(escort);
                check(MapComponent_CurseEncounters.Escorts(p)>=2,"two nearby colonists count as escorts");
                encounters.TickStalker(stalking);
                check(encounters.active==null && stalking.stalker.CurJob?.exitMapOnArrival==true && !stalking.stalker.InMentalState,"escorts cause retreat job without aggression");
                ally.Position=c+IntVec3.East*12; escort.Position=c+IntVec3.West*12;
                var attacker=Curse(m,c+IntVec3.North*9); made.Add(attacker); attacker.mindState.mentalStateHandler.CurState?.RecoverFromState();
                stalking=new CurseEncounter {kind=CurseEncounterKind.Stalker,stalker=attacker,target=p,endsAt=Find.TickManager.TicksGame+60000};
                encounters.active=stalking; encounters.TickStalker(stalking);
                check(encounters.active==null && attacker.MentalStateDef==MentalStateDefOf.ManhunterPermanent,"stalker commits to real hostility at deadline");
                attacker.mindState.mentalStateHandler.CurState?.RecoverFromState(); attacker.SetFaction(Faction.OfPlayer);
                encounters.active=stalking; encounters.TickStalker(stalking);
                check(encounters.active==null && !attacker.InMentalState,"captured stalker is never forced hostile");
                standing.nextOfferTick=0; standing.reputation=50; r.nextTrial=0;
                var downedTrial=gc.Accept(school,m,1,ContractKind.Infestation,p); made.AddRange(downedTrial.threats);
                p.health.AddHediff(HediffDefOf.Anesthetic); gc.Evaluate(downedTrial);
                check(p.Downed && downedTrial.state==ContractState.Failed,"downed candidate immediately fails trial evaluation");
            }
            catch(Exception e) { fail++; Log.Error("[JJK Study QA] "+e); }
            finally
            {
                encounters.active=oldEncounter; encounters.nextIncident=oldNext; gc.contracts=oldContracts; gc.standings=oldStanding;
                RimKaisenMod.Settings.gradeXpFactor=factor;
                LongEventHandler.ExecuteWhenFinished(()=> { foreach(var t in made)if(!t.Destroyed)t.Destroy(); });
            }
            Log.Message($"[JJK Study QA] RESULT {pass} passed, {fail} failed");
        }
        [DebugAction("Rim-Kaisen", "Prepare manual QA", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            var m=Find.CurrentMap; var c=JJK_DebugAdvancedPuppetChecks.Clear(m); var p=Human(m,c,"Manual Reader QA");
            JujutsuTraining.Class(p).UnlockTree(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech"));
            Item("JJK_TechniqueMasteryManual",m,c+IntVec3.East); Item("JJK_GradeTrainingManual",m,c+IntVec3.West);
            foreach(var school in GameComponent_Jujutsu.Schools) { var s=GameComponent_Jujutsu.Instance.Standing(school);s.reputation=50;s.nextOfferTick=0; }
            Log.Message("[JJK Study QA] fixture="+c+" reader="+p.ThingID);
        }
        [DebugAction("Rim-Kaisen", "Inspect study encounter QA", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Inspect()
        {
            var m=Find.CurrentMap; var p=m.mapPawns.FreeColonistsSpawned.First(x=>x.LabelShort=="Manual Reader QA");
            var r=GameComponent_SorcererStudy.Instance.For(p); var tree=DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech");
            var e=m.GetComponent<MapComponent_CurseEncounters>().active;
            Log.Message("[JJK Study QA] STATE job="+p.CurJob?.def.defName+" tree="+(p.jobs.curDriver as JobDriver_StudyManual)?.tree?.defName
                +" xp="+GameComponent_JJK.Instance.GetTechniqueXp(p,tree)+" gradeXP="+GameComponent_JJK.Instance.GetGradeXp(p)
                +" used="+(r.masteryBooks.TryGetValue(tree,out int n)?n:0)+" cooldown="+r.nextStudy+" encounter="+e?.kind+" source="+e?.nexus?.ThingID);
        }
        [DebugAction("Rim-Kaisen", "Prepare miasma QA", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Miasma()
        {
            var m=Find.CurrentMap; var c=JJK_DebugAdvancedPuppetChecks.Clear(m); var mc=m.GetComponent<MapComponent_CurseEncounters>();
            mc.active=new CurseEncounter {kind=CurseEncounterKind.Miasma,center=c+IntVec3.North*5,triggerAt=Find.TickManager.TicksGame+600,
                endsAt=Find.TickManager.TicksGame+60000,points=100};
        }
    }
}
