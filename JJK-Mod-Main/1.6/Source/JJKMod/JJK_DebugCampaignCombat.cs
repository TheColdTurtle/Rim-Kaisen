using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Disposable live-combat sampling. No hitpoint, resource or cooldown refills
    // after each fight starts; native movement, projectiles and enemy AI resolve it.
    public class MapComponent_CampaignCombat : MapComponent
    {
        private bool running;
        private int trial,started;
        private string file;
        private List<Pawn> team=new List<Pawn>(),enemies=new List<Pawn>();
        private readonly string[] moves={"JJK_DisasterFlames","JJK_Ratio_Strike","JJK_Speech_DontMove"};
        public MapComponent_CampaignCombat(Map map):base(map){}
        [DebugAction("Rim-Kaisen", "Start campaign combat samples",false,false,false,false,false,0,false,
            actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void StartSamples()=>Find.CurrentMap.GetComponent<MapComponent_CampaignCombat>().Begin();
        private void Begin()
        {
            if(running)return;
            if (!TryArena(out var at)) return;
            trial=0;file="combat-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".csv";
            JJK_DebugCampaignBalance.Csv(file,"trial","grade","budget","actual_enemy_points","elapsed_ticks","standing_allies","living_allies","remaining_enemies","ally_health","remaining_CE","outcome");
            try
            {
                Next(at); running=true; DebugSettings.godMode=false;
                Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;
            }
            catch (Exception ex) { running=false; Find.TickManager.CurTimeSpeed=TimeSpeed.Paused; Log.Error("[JJK Campaign Combat] SETUP FAILED " + ex); }
        }
        private bool TryArena(out IntVec3 at)
        {
            if (JJK_DebugAdvancedPuppetChecks.TryFindClear(map, out at)) return true;
            running = false; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Messages.Message("Combat QA needs a clear radius-10 area within 75 cells of the map center. Load the prepared QA arena.", MessageTypeDefOf.RejectInput, false);
            Log.Message("[JJK Campaign Combat] SETUP REJECTED: no clear arena; no empty-team result recorded.");
            return false;
        }
        internal bool Running => running;
        private void Next(IntVec3 at)
        {
            foreach(var p in team.Concat(enemies).Where(p=>p!=null))
            { if(p.Corpse?.Spawned==true)p.Corpse.Destroy();else if(!p.Destroyed)p.Destroy(); }
            team.Clear();enemies.Clear();
            int phase=trial%3;
            var grade=new[]{SorcererGrade.Grade4,SorcererGrade.Grade2,SorcererGrade.Special}[phase];
            for(int i=0;i<3;i++)
            {
                var p=JJK_DebugCampaignBalance.Human(map,at+new IntVec3(0,0,i*2),"Combat sample "+trial+" "+i,grade,
                    phase==0?"Average":"Deep",new[]{"DisasterFlames","Ratio","CursedSpeech"}[i]);
                foreach(var d in new[]{"Apparel_Pants","Apparel_BasicShirt","Apparel_Duster"})
                    p.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(d),ThingDefOf.Cloth));
                string weapon=i==1?"MeleeWeapon_LongSword":phase==0?"Gun_Revolver":"Gun_AssaultRifle";
                var wd=DefDatabase<ThingDef>.GetNamed(weapon);
                p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(wd,wd.MadeFromStuff?ThingDefOf.Steel:null));
                p.drafter.Drafted=true;team.Add(p);
                JJK_DebugCampaignBalance.Learn(p,DefDatabase<AbilityDef>.GetNamed(moves[i]));
                ExpansionTech.Energy(p).energy=ExpansionTech.Energy(p).MaxEnergy;
            }
            float budget=new[]{170f,600f,1600f}[phase];
            enemies.AddRange(CursedSpiritUtil.SpawnPack(map,CellFinder.RandomClosewalkCellNear(at+IntVec3.East*18,map,2),phase==2?budget-700:budget));
            if(phase==2)
            {
                var boss=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Boss_Jogo_Kind"));
                GenSpawn.Spawn(boss,CellFinder.RandomClosewalkCellNear(at+IntVec3.East*18,map,3),map);enemies.Add(boss);
            }
            started=Find.TickManager.TicksGame;
            Log.Message("[JJK Campaign Combat] START trial="+trial+" grade="+grade+" enemyPoints="+enemies.Sum(p=>p.kindDef.combatPower));
        }
        public override void MapComponentTick()
        {
            if(!running)return;
            try
            {
                int now=Find.TickManager.TicksGame;
                if(now%30==0)
                    for(int i=0;i<team.Count;i++)
                    {
                        var p=team[i];if(!p.Spawned||p.Downed||p.Dead||p.InMentalState||p.jobs.curDriver is TaranMagicFramework.JobDriver_CastAbility)continue;
                        var target=enemies.Where(e=>e.Spawned&&!e.Dead&&!e.Downed).OrderBy(e=>e.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                        if(target==null)continue;
                        var a=JJK_DebugCampaignBalance.Learn(p,DefDatabase<AbilityDef>.GetNamed(moves[i]));
                        if(p.Position.DistanceTo(target.Position)<=a.AbilityTier.effectRadius && GenSight.LineOfSight(p.Position,target.Position,map)
                            && a.CanBeActivated(a.EnergyCost,out _,false,a.CanBeActivatedValidator()))a.DoCast(target);
                        else if(i==1 && p.CurJobDef!=JobDefOf.AttackMelee)
                            p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee,target),JobCondition.InterruptForced);
                    }
                bool won=enemies.All(p=>p.Dead||p.Downed||!p.Spawned),lost=team.All(p=>p.Dead||p.Downed||!p.Spawned);
                if(won||lost||now-started>=6000)
                {
                    JJK_DebugCampaignBalance.Csv(file,trial,new[]{"Grade4","Grade2","Special"}[trial%3],new[]{170,600,1600}[trial%3],enemies.Sum(p=>p.kindDef.combatPower),now-started,
                        team.Count(p=>p.Spawned&&!p.Dead&&!p.Downed),team.Count(p=>!p.Dead),enemies.Count(p=>p.Spawned&&!p.Dead&&!p.Downed),
                        team.Sum(p=>p.Dead?0:p.health.summaryHealth.SummaryHealthPercent),team.Sum(p=>ExpansionTech.Energy(p)?.energy??0),won?"won":lost?"lost":"time limit");
                    Log.Message("[JJK Campaign Combat] END trial="+trial+" outcome="+(won?"won":lost?"lost":"time limit"));
                    trial++;if(trial<6){ if (TryArena(out var at)) Next(at); }else{running=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Log.Message("[JJK Campaign Combat] COMPLETE six native fights");}
                }
            }
            catch(Exception ex){running=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Log.Error("[JJK Campaign Combat] ERROR "+ex);}
        }
    }
}
