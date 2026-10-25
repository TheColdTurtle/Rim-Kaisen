using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Opt-in instrumentation for disposable saves. No balance modifiers or god-mode
    // immunity are applied while the native game ticks the campaign cohort.
    public static class JJK_DebugCampaignBalance
    {
        public const string Folder = "D:/Modding/Rimworld/JJK-Mod-Main/QA/CampaignBalance";
        public static void Csv(string file, params object[] row)
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, file), string.Join(",", row.Select(o => "\"" +
                Convert.ToString(o, CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"")) + Environment.NewLine);
        }
        public static Pawn Human(Map map, IntVec3 at, string name, SorcererGrade grade, string reserve, string technique)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 25));
            p.Name = new NameSingle(name);
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            foreach (var g in p.genes.GenesListForReading.Where(g => g.def.defName.StartsWith("JJK_")).ToList()) p.genes.RemoveGene(g);
            foreach (var t in p.apparel.WornApparel.ToList()) p.apparel.Remove(t);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_" + reserve + "Reserves"), true);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_" + technique), true);
            var comp = p.GetComp<CompAbilities>(); AccessTools.Field(typeof(CompAbilities), "frameChecking").SetValue(comp, 0);
            comp.RecheckAbilities(); GenSpawn.Spawn(p, at, map);
            GameComponent_JJK.Instance.SetGrade(p, grade); JujutsuTraining.Class(p).SetLevel((int)grade);
            p.needs.mood.CurLevel = .65f; ExpansionTech.Energy(p).energy = ExpansionTech.Energy(p).MaxEnergy;
            p.skills.GetSkill(SkillDefOf.Melee).Level = 10; p.skills.GetSkill(SkillDefOf.Shooting).Level = 10;
            return p;
        }
        public static Ability Learn(Pawn p, AbilityDef d)
        {
            var ac = JujutsuTraining.Class(p);
            foreach (var tree in d.abilityTrees) ac.UnlockTree(tree);
            return ac.GetLearnedAbility(d) ?? ac.LearnAbility(d, false);
        }
        [DebugAction("Rim-Kaisen", "Run campaign balance checks", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Checks()
        {
            var made = new List<Pawn>(); int passed=0,failed=0;
            Action<bool,string> check=(ok,label)=>{if(ok)passed++;else failed++;Log.Message("[JJK Campaign QA] "+(ok?"PASS ":"FAIL ")+label);};
            try
            {
                var map=Find.CurrentMap;var at=JJK_DebugAdvancedPuppetChecks.Clear(map);
                var p=Human(map,at,"Campaign allowance QA",SorcererGrade.Grade4,"Average","BoogieWoogie");made.Add(p);
                p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_SixEyes"),true);
                var a=Learn(p,DefDatabase<AbilityDef>.GetNamed("JJK_BoogieWoogie"));var gc=GameComponent_JJK.Instance;
                var tree=a.def.abilityTrees[0];int before=gc.GetGradeXp(p);
                for(int i=0;i<500;i++)Patch_Ability_Cast.Postfix(a,true);
                check(gc.GetGradeXp(p)-before==UnityEngine.Mathf.RoundToInt(120*(RimKaisenMod.Settings?.gradeXpFactor??1)),"500 paid activations award only 40 cast rewards");
                check(gc.GetTechniqueXp(p,tree)==80,"cast mastery capped at 80 per rolling day");
                check(gc.GetLastCastTree(p)==tree,"kill attribution survives exhausted practice allowance");
                int mastery=gc.GetTechniqueXp(p,tree);gc.AddTechniqueXp(p,tree,25);
                check(gc.GetTechniqueXp(p,tree)==mastery+25,"non-cast mastery remains available after cap");
                var animal=PawnGenerator.GeneratePawn(PawnKindDefOf.Muffalo,Faction.OfPlayer);made.Add(animal);
                GenSpawn.Spawn(animal,at+IntVec3.North*3,map);
                mastery=gc.GetTechniqueXp(p,tree);before=gc.GetGradeXp(p);
                animal.Kill(new DamageInfo(DamageDefOf.Cut,100,instigator:p));
                check(gc.GetTechniqueXp(p,tree)==mastery && gc.GetGradeXp(p)==before,"slaughtering owned livestock grants neither mastery nor grade XP");
                var curse=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade3_Kind"));made.Add(curse);
                GenSpawn.Spawn(curse,at+IntVec3.North*4,map);int killXp=JJK_Utils.GradeXpForKill(curse);
                curse.Kill(new DamageInfo(DamageDefOf.Cut,100,instigator:p));
                check(gc.GetTechniqueXp(p,tree)==mastery+killXp,"factionless hostile curse kill grants mastery beyond cast cap");
                check(gc.GetGradeXp(p)>before,"factionless hostile curse death retains grade credit");
                var q=Human(map,at+IntVec3.East,"Campaign free control QA",SorcererGrade.Grade4,"Average","BoogieWoogie");made.Add(q);
                check(!gc.TryRewardCastPractice(q,0),"free controls award no practice XP");
                check(gc.TryRewardCastPractice(q,.15f),"Six Eyes discounted paid casts remain eligible");
                var limit=new CastPracticeAllowance();int granted=0;
                for(int i=0;i<100;i++)if(limit.TryConsume(1000))granted++;
                check(granted==40,"allowance is shared and finite");
                check(!limit.TryConsume(60999),"allowance cannot reset before one full game day");
                check(limit.TryConsume(61000)&&limit.used==1,"allowance resets after 60000 ticks");
                check(!gc.TryRewardCastPractice(p,10),"saved fixture starts with exhausted allowance");
                // Retain one pawn for a real save/reload verification action.
                made.Remove(p);
                var energy=ExpansionTech.Energy(q);q.needs.mood.CurLevel=.65f;
                float normal=energy.EnergyGainPerTick()*60000;
                check(Math.Abs(normal-96)<.1f,"average Grade 4 recovers 96 CE per day");
                q.needs.mood.CurLevel=.1f;
                check(Math.Abs(energy.EnergyGainPerTick()*60000-normal*3)<.2f,"low-mood risk/reward retains triple recovery");
                q.needs.mood.CurLevel=.65f;
                q.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_BottomlessReserves"),true);
                float factor=StatPart_ReserveRecovery.Factor(q);
                check(Math.Abs(factor-Math.Sqrt(450f/140))<.001f,"bottomless reserve recovery has diminishing returns");
                GameComponent_JJK.Instance.SetGrade(q,SorcererGrade.Special);
                check(energy.MaxEnergy/(energy.EnergyGainPerTick()*60000)<3,"Special bottomless refill takes less than three normal-mood days");
                check(DefDatabase<ThingDef>.GetNamed("JJK_CommunicationShell").BaseMarketValue==400,"communication shell value corrected");
                check(DefDatabase<ThingDef>.GetNamed("JJK_AbsoluteShell").BaseMarketValue==9000,"giant frame value exceeds material value");
                var sash=DefDatabase<ThingDef>.GetNamed("JJK_SorcerersSash");
                check(sash.costList.Sum(x=>x.count*x.thingDef.BaseMarketValue)==780 && sash.GetStatValueAbstract(StatDefOf.WorkToMake)==12000,"endgame sash requires substantial materials and work");
            }
            catch(Exception ex){failed++;Log.Error("[JJK Campaign QA] EXCEPTION "+ex);}
            finally{foreach(var p in made.Where(p=>p!=null&&!p.Destroyed))p.Destroy();}
            Log.Message("[JJK Campaign QA] RESULT "+passed+" passed, "+failed+" failed");
        }
        [DebugAction("Rim-Kaisen", "Check campaign allowance after reload", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void ReloadCheck()
        {
            var p=Find.CurrentMap.mapPawns.AllPawnsSpawned.FirstOrDefault(x=>x.LabelShort=="Campaign allowance QA");
            Log.Message("[JJK Campaign QA] "+(p!=null&&!GameComponent_JJK.Instance.TryRewardCastPractice(p,10)?"PASS":"FAIL")+" exhausted cast allowance persists through native save/reload");
        }
        [DebugAction("Rim-Kaisen", "Export campaign balance audit", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Audit()
        {
            var map = Find.CurrentMap; Pawn p = null;
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            try
            {
                p = Human(map, JJK_DebugAdvancedPuppetChecks.Clear(map), "Balance audit", SorcererGrade.Grade4, "Average", "BoogieWoogie");
                Csv("abilities-"+stamp+".csv", "phase","ability","tree","class","required_grade","CE","cooldown_ticks","warmup_ticks","pool","recovery_days","projectile_damage_base","radius_or_range");
                foreach (var grade in new[]{SorcererGrade.Grade4,SorcererGrade.Grade2,SorcererGrade.Special})
                {
                    GameComponent_JJK.Instance.SetGrade(p, grade); JujutsuTraining.Class(p).SetLevel((int)grade);
                    foreach (var d in DefDatabase<AbilityDef>.AllDefs.Where(d=>d.defName.StartsWith("JJK_")).OrderBy(d=>d.defName))
                    {
                        var a = Learn(p,d); var t = a.AbilityTier; var ce = ExpansionTech.Energy(p);
                        Csv("abilities-"+stamp+".csv",grade,d.defName,string.Join(";",d.abilityTrees.Select(x=>x.defName)),d.abilityClass?.Name,
                            t.acquireRequirement?.minLevel ?? 0,a.EnergyCost,a.CooldownTicks,a.CastTicks,ce.MaxEnergy,
                            a.EnergyCost / Math.Max(.000001f,ce.EnergyGainPerTick())/60000f,
                            t.verbProperties?.FirstOrDefault()?.defaultProjectile?.projectile?.damageAmountBase ?? 0,t.effectRadius);
                    }
                }
                Csv("economy-"+stamp+".csv","def","market_value","work_to_make","fixed_material_value","stuff_count","recipe_outputs");
                foreach(var d in DefDatabase<ThingDef>.AllDefs.Where(d=>d.defName.StartsWith("JJK_") && d.category==ThingCategory.Item))
                    Csv("economy-"+stamp+".csv",d.defName,d.BaseMarketValue,d.GetStatValueAbstract(StatDefOf.WorkToMake),
                        d.costList?.Sum(c=>c.count*c.thingDef.BaseMarketValue) ?? 0,d.costStuffCount,
                        string.Join(";",DefDatabase<RecipeDef>.AllDefs.Where(r=>r.products.Any(x=>x.thingDef==d)).Select(r=>r.defName)));
                Csv("recipes-"+stamp+".csv","recipe","work","products_market_value","ingredient_options","research");
                foreach(var r in DefDatabase<RecipeDef>.AllDefs.Where(r=>r.defName.StartsWith("JJK_")||r.products.Any(x=>x.thingDef.defName.StartsWith("JJK_"))))
                    Csv("recipes-"+stamp+".csv",r.defName,r.workAmount,r.products.Sum(x=>x.count*x.thingDef.BaseMarketValue),
                        string.Join(";",r.ingredients.Select(i=>i.GetBaseCount()+":"+string.Join("|",i.filter.AllowedThingDefs.Select(d=>d.defName+"@"+d.BaseMarketValue)))),r.researchPrerequisite?.defName);
                Csv("spirits-"+stamp+".csv","kind","combat_points","health_scale","speed","residue","unique_boss","drop");
                foreach(var k in DefDatabase<PawnKindDef>.AllDefs.Where(k=>k.race.HasModExtension<CursedSpiritExtension>()))
                {
                    var e=k.race.GetModExtension<CursedSpiritExtension>();
                    Csv("spirits-"+stamp+".csv",k.defName,k.combatPower,k.race.race.baseHealthScale,k.race.GetStatValueAbstract(StatDefOf.MoveSpeed),e.residueOnExorcise,e.uniqueBoss,e.bossDrop?.defName);
                }
                Csv("summons-"+stamp+".csv","def","combat_points_per_cast","lifetime_days","taming","permanent_death");
                foreach(var s in DefDatabase<ShikigamiDef>.AllDefs)
                    Csv("summons-"+stamp+".csv",s.defName,s.pawnKind.combatPower*s.summonCount,s.summonDurationTicks/60000f,s.needsTaming,s.losableOnDeath);
                Csv("domains-"+stamp+".csv","ability","radius","open_CE","upkeep_CE_per_second","min_max_CE","raw_mastery","damage_base","interval","strain");
                foreach(var d in DefDatabase<AbilityDef>.AllDefs.Where(d=>d.HasModExtension<DomainExtension>()))
                {
                    var e=d.GetModExtension<DomainExtension>();
                    Csv("domains-"+stamp+".csv",d.defName,e.radius,d.abilityTiers[0].energyCost,e.upkeepPerTick*60,e.minMaxCE,e.masteryToOpen,e.pulseAmount,e.pulseIntervalTicks,e.sureHitStrain);
                }
                // Measure resource scale independently of the ability inventory.
                Csv("recovery-"+stamp+".csv","reserve","grade","max_CE","CE_per_day","empty_to_full_days","output");
                foreach(var reserve in new[]{"Shallow","Average","Deep","Bottomless"})
                {
                    foreach(var gene in p.genes.GenesListForReading.Where(g=>g is Gene_CursedEnergyReserve).ToList())p.genes.RemoveGene(gene);
                    p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_"+reserve+"Reserves"),true);
                    foreach(SorcererGrade grade in Enum.GetValues(typeof(SorcererGrade)))
                    {
                        GameComponent_JJK.Instance.SetGrade(p,grade);var ce=ExpansionTech.Energy(p);float perDay=ce.EnergyGainPerTick()*60000;
                        Csv("recovery-"+stamp+".csv",reserve,grade,ce.MaxEnergy,perDay,ce.MaxEnergy/Math.Max(.001f,perDay),p.GetStatValue(JJK_DefOf.JJK_CTOutput));
                    }
                }
                Log.Message("[JJK Campaign] AUDIT exported all loaded abilities, items, spirit kinds, shikigami, domains and 24 reserve/grade combinations; stamp="+stamp);
            }
            catch(Exception ex){Log.Message("[JJK Campaign] AUDIT ERROR "+ex);}
            finally { if(p!=null&&!p.Destroyed)p.Destroy(); }
        }

        [DebugAction("Rim-Kaisen", "Start four-day campaign balance cohort", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void StartCohort()
        {
            var map=Find.CurrentMap;var c=map.GetComponent<MapComponent_CampaignBalance>();
            if(c.running){Log.Message("[JJK Campaign] Already running");return;}
            var at=JJK_DebugAdvancedPuppetChecks.Clear(map);
            c.pawns.Clear();c.started=Find.TickManager.TicksGame;c.stopAt=c.started+240000;c.nextSample=c.started;
            // Disposable fixture barracks: ordinary shelter and finite heating fuel.
            for(int x=-4;x<=8;x++)for(int z=-4;z<=8;z++)
            {
                var cell=at+new IntVec3(x,0,z);
                map.areaManager.Home[cell]=true;
                if(x==-4||x==8||z==-4||z==8)
                    GenSpawn.Spawn(ThingMaker.MakeThing(x==2&&z==-4?ThingDefOf.Door:ThingDefOf.Wall,ThingDefOf.WoodLog),cell,map);
                else {map.roofGrid.SetRoof(cell,RoofDefOf.RoofConstructed);map.terrainGrid.SetTerrain(cell,DefDatabase<TerrainDef>.GetNamed("WoodPlankFloor"));}
            }
            var fire=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Campfire"));GenSpawn.Spawn(fire,at+new IntVec3(6,0,6),map);fire.TryGetComp<CompRefuelable>()?.Refuel(30);
            var fuel=ThingMaker.MakeThing(ThingDefOf.WoodLog);fuel.stackCount=75;GenPlace.TryPlaceThing(fuel,at+new IntVec3(5,0,6),map,ThingPlaceMode.Near);
            c.file="cohort-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".csv";
            Csv(c.file,"elapsed_days","pawn","grade_xp","mastery","CE","maxCE","regen_per_day","mood","food","health","downed","dead","job","hostiles_on_map");
            int index=0;
            foreach(var grade in new[]{SorcererGrade.Grade4,SorcererGrade.Grade2,SorcererGrade.Special})
                foreach(var role in new[]{"Recovery","Practice","Clapping"})
                {
                    var p=Human(map,at+IntVec3.East*(index%3)*2+IntVec3.North*(index/3)*2,
                        "Campaign "+grade+" "+role,grade,grade==SorcererGrade.Grade4?"Average":"Deep","BoogieWoogie");index++;
                    c.pawns.Add(p);ExpansionTech.Energy(p).energy=role=="Recovery"?0:ExpansionTech.Energy(p).MaxEnergy;
                    var food=ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack);food.stackCount=16;p.inventory.innerContainer.TryAdd(food);
                    var bed=ThingMaker.MakeThing(ThingDefOf.Bed,ThingDefOf.WoodLog);GenPlace.TryPlaceThing(bed,p.Position,map,ThingPlaceMode.Near);
                    var clothes=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Parka"),ThingDefOf.Cloth) as Apparel;p.apparel.Wear(clothes);
                    if(role=="Practice")
                    {
                        var plan=GameComponent_Jujutsu.Instance.Plan(p);plan.enabled=true;plan.tree=DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_BoogieWoogie");
                        plan.hour=10;
                        p.workSettings.SetPriority(WorkTypeDefOf.Research,1);
                        var post=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_TrainingPost"));GenPlace.TryPlaceThing(post,p.Position,map,ThingPlaceMode.Near);
                    }
                }
            var six=Human(map,at+IntVec3.South*2,"Campaign SixEyes Clapping",SorcererGrade.Grade4,"Average","BoogieWoogie");
            six.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_SixEyes"),true);c.pawns.Add(six);
            var rations=ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack);rations.stackCount=16;six.inventory.innerContainer.TryAdd(rations);
            // Supply a common ideology, complete ordinary clothing and accessible
            // ground food. This is a controlled supplied camp, not a built colony.
            foreach(var p in c.pawns)
            {
                if(ModsConfig.IdeologyActive && Faction.OfPlayer.ideos?.PrimaryIdeo!=null)p.ideo.SetIdeo(Faction.OfPlayer.ideos.PrimaryIdeo);
                foreach(var def in new[]{"Apparel_Pants","Apparel_BasicShirt","Apparel_Parka"})
                    if(!p.apparel.WornApparel.Any(a=>a.def.defName==def))p.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def),ThingDefOf.Cloth));
                var meal=ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack);meal.stackCount=40;GenPlace.TryPlaceThing(meal,p.Position,map,ThingPlaceMode.Near);
                p.playerSettings.medCare=MedicalCareCategory.Best;
                var b=ThingMaker.MakeThing(ThingDefOf.Bed,ThingDefOf.WoodLog);GenPlace.TryPlaceThing(b,p.Position,map,ThingPlaceMode.Near);
            }
            c.running=true;DebugSettings.godMode=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;
            Log.Message("[JJK Campaign] START 240000 native ticks, 9 early/mid/late cohorts plus Six Eyes utility-cast probe; god mode off; supplies given once, no later CE/health/needs refills. File="+c.file);
        }
        [DebugAction("Rim-Kaisen", "Report campaign balance cohort", false,false,false,false,false,0,false,
            actionType=DebugActionType.Action, allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Report()=>Find.CurrentMap.GetComponent<MapComponent_CampaignBalance>().Sample();
    }

    public class MapComponent_CampaignBalance : MapComponent
    {
        public bool running;public int started,stopAt,nextSample;public string file;
        public List<Pawn> pawns=new List<Pawn>();
        public MapComponent_CampaignBalance(Map map):base(map){}
        public override void ExposeData()
        {
            Scribe_Values.Look(ref running,"campaignBalanceRunning");Scribe_Values.Look(ref started,"campaignBalanceStart");
            Scribe_Values.Look(ref stopAt,"campaignBalanceStop");Scribe_Values.Look(ref nextSample,"campaignBalanceSample");Scribe_Values.Look(ref file,"campaignBalanceFile");
            Scribe_Collections.Look(ref pawns,"campaignBalancePawns",LookMode.Reference);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)pawns=pawns??new List<Pawn>();
        }
        public void Sample()
        {
            if(string.IsNullOrEmpty(file))return;
            int now=Find.TickManager.TicksGame;
            foreach(var p in pawns.Where(p=>p!=null))
            {
                var ce=ExpansionTech.Energy(p);
                JJK_DebugCampaignBalance.Csv(file,(now-started)/60000f,p.LabelShort,GameComponent_JJK.Instance.GetGradeXp(p),
                    GameComponent_JJK.Instance.GetTechniqueXp(p,DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_BoogieWoogie")),ce?.energy,ce?.MaxEnergy,
                    ce?.EnergyGainPerTick()*60000,p.needs?.mood?.CurLevel,p.needs?.food?.CurLevelPercentage,
                    p.health?.summaryHealth.SummaryHealthPercent,p.Downed,p.Dead,p.CurJobDef?.defName,map.mapPawns.AllPawnsSpawned.Count(x=>x.HostileTo(Faction.OfPlayer)));
            }
            Log.Message("[JJK Campaign] SAMPLE day="+((now-started)/60000f).ToString("0.00")+" alive="+pawns.Count(p=>p!=null&&!p.Dead)+" running="+running);
        }
        public override void MapComponentTick()
        {
            if(!running)return;int now=Find.TickManager.TicksGame;
            if(now>=nextSample){Sample();nextSample=now+15000;}
            // Repeated utility-use probe goes through real warmup, cost and cooldown.
            if(now%60==0)
                foreach(var p in pawns.Where(p=>p?.Spawned==true&&!p.Dead&&!p.Downed&&!p.InMentalState&&p.LabelShort.Contains("Clapping")))
                {
                    // Do not interrupt eating, sleeping or recreation with the probe.
                    if(p.jobs.curDriver is TaranMagicFramework.JobDriver_CastAbility || p.needs.food.CurLevelPercentage<.7f || p.needs.rest.CurLevelPercentage<.7f || p.needs.joy.CurLevelPercentage<.4f)continue;
                    var d=DefDatabase<AbilityDef>.GetNamed("JJK_BoogieWoogie");var a=JJK_DebugCampaignBalance.Learn(p,d);
                    var target=pawns.FirstOrDefault(o=>o!=p&&o.Spawned&&!o.Dead&&o.Position.DistanceToSquared(p.Position)<100);
                    if(target!=null&&a.CanBeActivated(a.EnergyCost,out _,false,a.CanBeActivatedValidator()))a.DoCast(target);
                }
            if(now>=stopAt){running=false;Sample();Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Log.Message("[JJK Campaign] COMPLETE four native game days");}
        }
    }
}
