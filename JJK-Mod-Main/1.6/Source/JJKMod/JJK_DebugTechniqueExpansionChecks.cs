using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugTechniqueExpansionChecks
    {
        private static Pawn Human(Map map,IntVec3 cell,string technique,string name,bool hostile=false)
        {
            var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
            foreach(var h in p.health.hediffSet.hediffs.ToList())p.health.RemoveHediff(h);
            foreach(var g in p.genes.GenesListForReading.Where(g=>g.def.defName.StartsWith("JJK_")).ToList())p.genes.RemoveGene(g);
            p.Name=new NameSingle(name);p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"),true);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_"+technique),true);
            // Fixture setup performs many gene operations in one frame; reset only
            // this synthetic pawn's TMF refresh throttle before its final scan.
            var comp=p.GetComp<CompAbilities>();AccessTools.Field(typeof(CompAbilities),"frameChecking").SetValue(comp,0);
            comp.RecheckAbilities();GenSpawn.Spawn(p,cell,map);
            GameComponent_JJK.Instance.SetGrade(p,SorcererGrade.Special);JujutsuTraining.Class(p).SetLevel(4);
            GameComponent_JJK.Instance.AddTechniqueXp(p,DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_"+technique),3000);
            ExpansionTech.Energy(p).energy=500;
            if(hostile)p.SetFaction(Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("JJK_CurseUsers")));
            return p;
        }
        private static Ability Learn(Pawn p,string name)
        {
            var d=DefDatabase<AbilityDef>.GetNamed(name);var ac=JujutsuTraining.Class(p);foreach(var t in d.abilityTrees)ac.UnlockTree(t);
            return ac.GetLearnedAbility(d)??ac.LearnAbility(d,false);
        }
        private static Ability Cast(Pawn p,string move,LocalTargetInfo target=default(LocalTargetInfo))
        {var a=Learn(p,"JJK_X_"+move);a.curTarget=target.IsValid?target:p;a.Start(false);return a;}
        private static bool Has(Pawn p,string h)=>p.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_"+h));
        [DebugAction("Rim-Kaisen","Run technique expansion checks",false,false,false,false,false,0,false,actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass=0,fail=0;var m=Find.CurrentMap;var made=new List<Thing>();var gc=GameComponent_TechniqueExpansion.Instance;
            Action<bool,string> check=(ok,name)=>{if(ok)pass++;else fail++;Log.Message("[JJK Technique QA] "+(ok?"PASS ":"FAIL ")+name);};
            try
            {
                var c=JJK_DebugAdvancedPuppetChecks.Clear(m);
                var p=Human(m,c,"Construction","Construction QA");made.Add(p);var s=ExpansionTech.State(p);
                Cast(p,"Bullet");check(ExpansionTech.Supply(p,"JJK_ConstructedBullet")!=null&&s.constructNext>ExpansionTech.Now,"construction produces persistent inventory ammo");
                check(((Ability_ExpansionMove)Learn(p,"JJK_X_Blade")).Reason(false)!=null,"shared daily material production cap");
                s.constructNext=0;Cast(p,"Blade");check(p.inventory.innerContainer.Any(t=>t.def.defName=="JJK_ConstructedBlade"&&t.Stuff==ThingDefOf.Steel),"construction creates usable persistent steel blade");
                var enemy=Human(m,c+IntVec3.East*2,"CursedSpeech","Target QA",true);made.Add(enemy);
                Cast(p,"FireBullet",enemy);check(ExpansionTech.Supply(p,"JJK_ConstructedBullet")==null,"firing consumes actual ammo");
                Cast(p,"Armor");check(Has(p,"InsectArmor"),"constructed armor applies");
                Cast(p,"Sphere",c+IntVec3.North*5);check(m.GetComponent<MapComponent_ExpansionBlasts>().pending.Any(b=>b.sphere&&b.at>ExpansionTech.Now),"sphere queues saved warning before damage");
                var sky=Human(m,c+IntVec3.West,"SkyManipulation","Sky QA");made.Add(sky);Cast(sky,"SkyGuard");
                var projectile=(Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_Proj_ConstructedBullet"),sky.Position+IntVec3.North,m);
                projectile.Launch(enemy,sky,sky,ProjectileHitFlags.IntendedTarget);made.Add(projectile);float ce=ExpansionTech.Energy(sky).energy;
                projectile.Tick();check(gc.reflected.Contains(projectile)&&ExpansionTech.Energy(sky).energy==ce-5,"actual projectile tick redirects and pays CE");
                projectile.Tick();check(ExpansionTech.Energy(sky).energy==ce-5,"same projectile is not reflected twice");
                Cast(sky,"SkyDistort",enemy);check(Has(enemy,"SkyDistortion"),"sky distortion impairs sight");
                var ritual=Human(m,c+IntVec3.South,"SoloForbiddenArea","Ritual QA");made.Add(ritual);Cast(ritual,"Ritual");var rs=ExpansionTech.State(ritual);
                gc.TickState(rs);check(Has(p,"RitualMinor"),"nearby ally receives ritual support");rs.ritualStart-=300;gc.TickState(rs);
                check(Has(p,"RitualFull")&&!Has(p,"RitualMinor"),"uninterrupted ritual upgrades without stacking");
                ritual.TakeDamage(new DamageInfo(DamageDefOf.Blunt,1,0,-1,enemy));check(rs.ritualUntil==0,"real damage interrupts channel");
                var reggie=Human(m,c+IntVec3.South*2,"ContractualRecreation","Receipt QA");made.Add(reggie);
                check(((Ability_ExpansionMove)Learn(reggie,"JJK_X_ReceiptRest")).Reason(false)!=null,"missing receipt blocks casting");
                ExpansionTech.Place(reggie,"JJK_ReceiptRest");reggie.needs.rest.CurLevel=.1f;Cast(reggie,"ReceiptRest");
                check(reggie.needs.rest.CurLevel>.69f&&ExpansionTech.Supply(reggie,"JJK_ReceiptRest")==null,"receipt consumed for actual rest");
                var inverse=Human(m,c+IntVec3.West*2,"Inverse","Inverse QA");made.Add(inverse);Cast(inverse,"Inverse");
                var torso=inverse.RaceProps.body.corePart;
                var result=inverse.TakeDamage(new DamageInfo(DamageDefOf.Blunt,60,100,-1,enemy,torso));
                check(result.totalDamageDealt>0&&result.totalDamageDealt<4,"real damage hook weakens strong hit with grade resistance");
                ExpansionTech.Heal(inverse,100);
                result=inverse.TakeDamage(new DamageInfo(DamageDefOf.Blunt,2,100,-1,enemy,torso));
                check(result.totalDamageDealt>10&&result.totalDamageDealt<=25,"real damage hook amplifies weak hit with grade resistance");ExpansionTech.Heal(inverse,100);
                check(Patch_InverseDamage.Invert(101)==101,"overwhelming hit bypasses inverse");
                var love=Human(m,c+IntVec3.North,"LoveRendezvous","Star QA");made.Add(love);var ls=ExpansionTech.State(love);
                ls.nextStar=0;StarTechnique.Mark(ls,p);ls.nextStar=3;StarTechnique.Mark(ls,enemy);
                check(!StarTechnique.MayMove(p,p.Position+IntVec3.East),"skipped stars block approach");ls.marks.First(x=>x.target==p).progress=2;
                check(StarTechnique.MayMove(p,p.Position+IntVec3.East),"visited route permits approach");Cast(love,"StarClear");check(ls.marks.Count==0,"release removes all star restrictions");
                ls.nextStar=0;StarTechnique.Mark(ls,p);StarTechnique.Mark(ls,enemy);p.Position=c+IntVec3.West*4;var oldPosition=p.Position;var oldEnemyPosition=enemy.Position;StarTechnique.Tick(ls);
                check(p.Position!=oldPosition||enemy.Position!=oldEnemyPosition,"matching stars pull a lower or equal reserve pawn");p.Position=c;ls.marks.Clear();
                var gravity=Human(m,c+IntVec3.North*2,"Antigravity","Gravity QA");made.Add(gravity);
                foreach(var trait in gravity.story.traits.allTraits.Where(t=>t.def==JJK_DefOf.JJK_RCT).ToList())gravity.story.traits.RemoveTrait(trait);
                check(((Ability_ExpansionMove)Learn(gravity,"JJK_X_Gravity")).Reason(false)!=null,"gravity reversal requires RCT");
                gravity.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_RCT));ExpansionTech.Buff(enemy,"JJK_Antigravity",900);Cast(gravity,"Gravity");
                projectile=(Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_Proj_ConstructedBullet"),gravity.Position+IntVec3.North*3,m);projectile.Launch(enemy,gravity,gravity,ProjectileHitFlags.IntendedTarget);made.Add(projectile);
                projectile.Tick();check(projectile.Destroyed,"gravity field grounds real incoming projectile");
                check(!Has(enemy,"GravityCrush"),"antigravity resists reversal crush");ExpansionTech.ClearBuff(enemy,"JJK_Antigravity");Cast(gravity,"Gravity");check(Has(enemy,"GravityCrush"),"unprotected hostile receives gravity crush");
                var angel=Human(m,c+IntVec3.East,"Extinguishment","Angel QA");made.Add(angel);Cast(angel,"Extinguish",enemy);check(TechniqueSealUtil.IsSealed(enemy),"Angel suppresses hostile techniques");
                check(((Ability_ExpansionMove)Learn(angel,"JJK_X_BreakBarrier")).Reason(true)!=null,"barrier break rejects absent domain");
                var copy=Human(m,c+IntVec3.South*3,"Copy","Copy QA");made.Add(copy);Cast(copy,"Rika");var cs=ExpansionTech.State(copy);made.Add(cs.rika);
                check(cs.RikaActive&&ExpansionTech.EnergyOwner(cs.rika)==copy,"Rika manifests with real owner bond");
                var donor=Human(m,copy.Position+IntVec3.East,"CursedSpeech","Copy donor QA",true);made.Add(donor);
                HealthUtility.DamageUntilDowned(donor);cs.rika.Position=copy.Position+IntVec3.North;
                var gene=DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_CursedSpeech");var part=CopyTechnique.SamplePart(donor);
                check(CopyTechnique.Sample(cs,donor,gene)&&donor.health.hediffSet.PartIsMissing(part),"copy consumes actual donor limb");
                check(cs.SelectCopy(gene),"stored sample grants temporary loan");var speech=JujutsuTraining.Class(copy).GetLearnedAbility(DefDatabase<AbilityDef>.GetNamed("JJK_Speech_DontMove"));
                check(speech!=null&&!copy.genes.HasActiveGene(gene),"borrowed ability learned without original innate gene");
                check(!StudyManuals.OwnedTrees(copy).Contains(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech")),"borrowed tree excluded from innate study");
                if(speech!=null)
                {
                    speech.curTarget=enemy;speech.Start(false);check(cs.copies[0].uses==2,"real borrowed cast consumes one use");
                    int cooldown=speech.cooldownPeriod;cs.SelectCopy(gene);speech=JujutsuTraining.Class(copy).GetLearnedAbility(speech.def);
                    check(speech.cooldownPeriod>=cooldown&&cs.copies[0].uses==2,"reselecting preserves cooldown and use count");
                    speech.curTarget=enemy;speech.Start(false);speech.Start(false);gc.TickState(cs);
                    check(cs.copies[0].uses==0&&cs.borrowed==null&&!CopyTechnique.Allowed(speech),"third cast exhausts imprint and blocks queued old ability");
                    cs.copies[0].uses=1;cs.SelectCopy(gene);
                }
                donor.health.RestorePart(part);gc.TickState(cs);check(cs.borrowed==null,"donor regrowth revokes borrowed power");
                Cast(copy,"DismissRika");check(!cs.RikaActive&&cs.rikaCooldown>ExpansionTech.Now,"dismiss ends Rika without resetting cooldown");
                check(!JJK_Utils.CursedTechniqueGenes.Any(g=>g.geneClass==typeof(Gene_CopiedTechnique)),"random pool excludes temporary loans");
                var hakari=Human(m,c+IntVec3.West*3,"Pachinko","Pachinko QA");made.Add(hakari);var hs=ExpansionTech.State(hakari);
                var gamble=Learn(hakari,"JJK_Domain_IdleDeathGamble");gamble.Start(false);var dm=m.GetComponent<MapComponent_DomainManager>();var d=dm.DomainFor(hakari);
                check(hs.gambleStart>0,"entire gamble is rolled at opening before first tick or save");
                check(d?.Props.gambling==true,"real gambling domain opens");gc.TickState(hs);hs.gambleWin=1;hs.gambleStart=ExpansionTech.Now-180;gc.TickGamble(hs,d);
                check(hs.jackpotUntil==ExpansionTech.Now+15060&&!dm.HasDomain(hakari)&&!Has(hakari,"DomainBurnout"),"winning closes domain with 251-second jackpot and no burnout");
                gc.TickState(hs);check(ExpansionTech.Energy(hakari).energy>10000,"jackpot refills expanded actual CE reserve");
                HealthUtility.DamageUntilDowned(hakari);bool wasDowned=hakari.Downed;for(int i=0;i<8;i++)gc.TickState(hs);
                check(wasDowned&&!hakari.Dead&&!hakari.Downed&&hs.jackpotUntil>ExpansionTech.Now,"jackpot healing recovers a downed living pawn");
                gamble.Start(false);check(!dm.HasDomain(hakari),"jackpot blocks another gamble");hs.jackpotUntil=0;ExpansionTech.ClearBuff(hakari,"JJK_Jackpot");gamble.Start(false);d=dm.DomainFor(hakari);gc.TickState(hs);hs.gambleWin=0;hs.gambleStart=ExpansionTech.Now-1080;gc.TickGamble(hs,d);
                check(!dm.HasDomain(hakari)&&Has(hakari,"DomainBurnout"),"six failed spins cause burnout");
                var constructionDomain=Learn(p,"JJK_Domain_ThreefoldAffliction");constructionDomain.Start(false);
                check(dm.DomainFor(p)?.sourceAbility==constructionDomain.def,"Threefold Affliction opens through real ability");
                // Change only the test subject's allegiance so Angel can target this domain.
                p.SetFaction(enemy.Faction);var breaker=(Ability_ExpansionMove)Learn(angel,"JJK_X_BreakBarrier");breaker.curTarget=p;
                check(breaker.Reason(true)==null,"qualified Angel can target hostile constructed domain");breaker.Start(false);
                check(!dm.HasDomain(p)&&Has(p,"DomainBurnout"),"Angel barrier break actually collapses hostile domain");
            }
            catch(Exception e){fail++;Log.Error("[JJK Technique QA] "+e);}
            finally
            {
                m.GetComponent<MapComponent_ExpansionBlasts>().pending.RemoveAll(b=>made.Contains(b.caster));
                LongEventHandler.ExecuteWhenFinished(()=>{foreach(var t in made.Where(t=>t!=null&&!t.Destroyed).ToList()){if(t is Pawn p)m.GetComponent<MapComponent_DomainManager>().CollapseFor(p,true);t.Destroy();}gc.states.RemoveAll(s=>made.Contains(s.pawn));});
                Log.Message("[JJK Technique QA] RESULT "+pass+" passed, "+fail+" failed");
            }
        }
        [DebugAction("Rim-Kaisen","Prepare technique persistence fixture",false,false,false,false,false,0,false,actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Fixture()
        {
            var m=Find.CurrentMap;var c=JJK_DebugAdvancedPuppetChecks.Clear(m);
            var p=Human(m,c,"Copy","Expansion Copy QA");Cast(p,"Rika");var s=ExpansionTech.State(p);
            var donor=Human(m,c+IntVec3.East,"CursedSpeech","Expansion donor QA",true);HealthUtility.DamageUntilDowned(donor);s.rika.Position=c+IntVec3.North;
            var gene=DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_CursedSpeech");CopyTechnique.Sample(s,donor,gene);s.SelectCopy(gene);
            var h=Human(m,c+IntVec3.West*4,"Pachinko","Expansion Hakari QA");Learn(h,"JJK_Domain_IdleDeathGamble").Start(false);var hs=ExpansionTech.State(h);GameComponent_TechniqueExpansion.Instance.TickState(hs);hs.gambleWin=2;
            var love=Human(m,c+IntVec3.South*3,"LoveRendezvous","Expansion Kirara QA");StarTechnique.Mark(ExpansionTech.State(love),p);
            m.GetComponent<MapComponent_ExpansionBlasts>().Queue(love,c+IntVec3.North*8,3000,3,30,true);
            Log.Message("[JJK Technique QA] Fixture ready: borrowed speech, Rika, saved second-spin win, star mark and delayed sphere.");
        }
        [DebugAction("Rim-Kaisen","Verify technique persistence fixture",false,false,false,false,false,0,false,actionType=DebugActionType.Action,allowedGameStates=AllowedGameStates.PlayingOnMap)]
        private static void Verify()
        {
            var gc=GameComponent_TechniqueExpansion.Instance;var s=gc.states.First(s=>s.pawn.LabelShort=="Expansion Copy QA");
            var h=gc.states.First(s=>s.pawn.LabelShort=="Expansion Hakari QA");var love=gc.states.First(s=>s.pawn.LabelShort=="Expansion Kirara QA");
            bool ok=s.RikaActive&&s.borrowed!=null&&s.copies.Single().Valid&&s.copies[0].uses==3&&h.gambleWin==2&&love.marks.Count==1
                &&Find.CurrentMap.GetComponent<MapComponent_ExpansionBlasts>().pending.Count>0;
            Log.Message("[JJK Technique QA] "+(ok?"PASS":"FAIL")+" save/load retains Rika, loan, donor limb, uses, predetermined spin, star and blast");
        }
    }
}



