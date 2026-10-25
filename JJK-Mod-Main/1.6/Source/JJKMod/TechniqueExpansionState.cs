using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public class TechniqueRarity : DefModExtension { public float weight = 1f; }
    public class Gene_CopiedTechnique : Gene_CursedTechnique
    {
        public override void Tick()
        {
            base.Tick();
            if (pawn.IsHashIntervalTick(60) && GameComponent_TechniqueExpansion.Instance?.states.Any(s=>s.pawn==pawn && s.borrowed==def && s.RikaActive)!=true)
                pawn.genes.RemoveGene(this);
        }
    }
    public class StarMark : IExposable
    {
        public Pawn target; public int star, progress, until;
        public void ExposeData() { Scribe_References.Look(ref target,"target"); Scribe_Values.Look(ref star,"star"); Scribe_Values.Look(ref progress,"progress"); Scribe_Values.Look(ref until,"until"); }
    }
    public class CopyImprint : IExposable
    {
        public GeneDef gene; public Pawn source; public int partIndex, uses = 3; public bool sourceDead;
        public Dictionary<AbilityDef,int> cooldowns = new Dictionary<AbilityDef,int>();
        public bool Valid => uses > 0 && gene != null && (sourceDead || source != null && (source.Dead
            || partIndex >= 0 && partIndex < source.RaceProps.body.AllParts.Count && source.health.hediffSet.PartIsMissing(source.RaceProps.body.AllParts[partIndex])));
        public void ExposeData()
        {
            if(Scribe.mode==LoadSaveMode.Saving && source!=null) { sourceDead |= source.Dead; if(source.Destroyed)source=null; }
            Scribe_Defs.Look(ref gene,"gene"); Scribe_References.Look(ref source,"source"); Scribe_Values.Look(ref partIndex,"partIndex");
            Scribe_Values.Look(ref uses,"uses",3); Scribe_Values.Look(ref sourceDead,"sourceDead");
            Scribe_Collections.Look(ref cooldowns,"cooldowns",LookMode.Def,LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)cooldowns=cooldowns??new Dictionary<AbilityDef,int>();
        }
    }
    public class TechniqueState : IExposable
    {
        public Pawn pawn, rika;
        public int skyUntil, inverseUntil, gravityUntil, ritualUntil, ritualStart, rikaUntil, rikaCooldown, constructNext, receiptNext;
        public int jackpotUntil, gambleStart, gambleSpin, gambleWin, gambleSerial, nextStar;
        public IntVec3 ritualCell;
        public List<StarMark> marks=new List<StarMark>();
        public List<CopyImprint> copies=new List<CopyImprint>();
        public GeneDef selectedCopy, borrowed;
        public bool Unsealed => pawn?.Spawned==true && !pawn.Dead && !TechniqueSealUtil.IsSealed(pawn)
            && !DomainAmplificationUtil.Active(pawn) && !pawn.health.hediffSet.HasHediff(JJK_DefOf.JJK_DomainBurnout);
        public bool Operational => Unsealed && !pawn.Downed;
        public bool RikaActive => Operational && Has("Copy") && rika?.Spawned==true && !rika.Dead && rika.Map==pawn.Map && rikaUntil>ExpansionTech.Now;
        public bool Has(string tree) => pawn?.genes?.HasActiveGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_"+tree))==true
            || borrowed!=null && selectedCopy?.defName=="JJK_CT_Gene_"+tree && pawn?.genes?.HasActiveGene(borrowed)==true;
        public void RemoveLoan()
        {
            if(borrowed==null)return;
            var gene=pawn?.genes?.GetGene(borrowed);
            if(gene!=null)
            {
                foreach(var a in JujutsuTraining.Class(pawn).LearnedAbilities.OfType<Ability_Toggleable>().Where(a=>a.Active&&CopyTechnique.IsLoan(a,this)).ToList())a.End();
                var ac=JujutsuTraining.Class(pawn);
                var trees=borrowed.GetModExtension<AbilityExtension>().abilityTrees.ToList();
                var imprint=copies.FirstOrDefault(c=>c.gene==selectedCopy);
                foreach(var a in ac.LearnedAbilities.Where(a=>CopyTechnique.IsLoan(a,this)).ToList())
                {if(imprint!=null)imprint.cooldowns[a.def]=Math.Max(imprint.cooldowns.TryGetValue(a.def,out int ready)?ready:0,a.lastActivatedTick+a.cooldownPeriod);ac.RemoveAbility(a);}
                borrowed=null;pawn.genes.RemoveGene(gene);
                foreach(var tree in trees)
                    if(!JJK_Utils.CursedTechniqueGenes.Any(g=>pawn.genes.HasActiveGene(g)&&g.GetModExtension<AbilityExtension>()?.abilityTrees?.Contains(tree)==true))ac.LockTree(tree);
                pawn.GetComp<CompAbilities>().RecheckAbilities();
            }
            borrowed=null;
        }
        public bool SelectCopy(GeneDef selected)
        {
            if(!RikaActive || pawn.genes.HasActiveGene(selected) || !copies.Any(c=>c.gene==selected && c.Valid))return false;
            RemoveLoan(); selectedCopy=selected;
            var loan=DefDatabase<GeneDef>.GetNamedSilentFail("JJK_CopyLoan_"+selected.defName);
            if(loan==null)return false;
            borrowed=loan; pawn.genes.AddGene(loan,true);
            var ac=JujutsuTraining.Class(pawn);var trees=loan.GetModExtension<AbilityExtension>().abilityTrees;
            foreach(var tree in trees)ac.UnlockTree(tree);
            foreach(var def in DefDatabase<AbilityDef>.AllDefsListForReading.Where(d=>d.abilityTrees?.Any(trees.Contains)==true && d.GetModExtension<DomainExtension>()==null))
                if(def.abilityTiers.Count>0 && def.abilityTiers[0].autoGain && (def.abilityTiers[0].acquireRequirement?.RequirementSatisfied(ac,null)??true))
                    ac.LearnAbility(def,false);
            var imprint=copies.First(c=>c.gene==selected);
            foreach(var a in JujutsuTraining.Class(pawn).LearnedAbilities.Where(a=>IsBorrowedAbility(a)))
                if(imprint.cooldowns.TryGetValue(a.def,out int ready))a.SetCooldown(Math.Max(0,ready-ExpansionTech.Now));
            return true;
        }
        private bool IsBorrowedAbility(TaranMagicFramework.Ability a)=>CopyTechnique.IsLoan(a,this);
        public void ExposeData()
        {
            if(Scribe.mode==LoadSaveMode.Saving) { if(rika?.Destroyed==true)rika=null; marks.RemoveAll(m=>m.target==null || m.target.Destroyed); }
            Scribe_References.Look(ref pawn,"pawn"); Scribe_References.Look(ref rika,"rika");
            Scribe_Values.Look(ref skyUntil,"skyUntil"); Scribe_Values.Look(ref inverseUntil,"inverseUntil"); Scribe_Values.Look(ref gravityUntil,"gravityUntil");
            Scribe_Values.Look(ref ritualUntil,"ritualUntil"); Scribe_Values.Look(ref ritualStart,"ritualStart"); Scribe_Values.Look(ref ritualCell,"ritualCell");
            Scribe_Values.Look(ref rikaUntil,"rikaUntil"); Scribe_Values.Look(ref rikaCooldown,"rikaCooldown"); Scribe_Values.Look(ref constructNext,"constructNext"); Scribe_Values.Look(ref receiptNext,"receiptNext");
            Scribe_Values.Look(ref jackpotUntil,"jackpotUntil"); Scribe_Values.Look(ref gambleStart,"gambleStart"); Scribe_Values.Look(ref gambleSpin,"gambleSpin");
            Scribe_Values.Look(ref gambleWin,"gambleWin"); Scribe_Values.Look(ref gambleSerial,"gambleSerial"); Scribe_Values.Look(ref nextStar,"nextStar");
            Scribe_Defs.Look(ref selectedCopy,"selectedCopy"); Scribe_Defs.Look(ref borrowed,"borrowed");
            Scribe_Collections.Look(ref copies,"copies",LookMode.Deep); Scribe_Collections.Look(ref marks,"marks",LookMode.Deep);
            if(Scribe.mode==LoadSaveMode.PostLoadInit) { copies=copies??new List<CopyImprint>();marks=marks??new List<StarMark>(); }
        }
    }
    public static class ExpansionTech
    {
        public static int Now => Find.TickManager.TicksGame;
        public static TechniqueState State(Pawn p) => GameComponent_TechniqueExpansion.Instance.For(p);
        public static AbilityResource Energy(Pawn p) => p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
        public static bool Pay(Pawn p,float amount) { var e=Energy(p); if(e==null || e.energy<amount)return false; e.energy-=amount;return true; }
        public static Hediff Buff(Pawn p,string name,int ticks)
        {
            var d=DefDatabase<HediffDef>.GetNamed(name);
            string phenomenon = MahoragaPhenomena.ControlKey(d);
            if (phenomenon != null) { ticks = MahoragaPhenomena.ControlTicks(p, ticks, phenomenon); if (ticks <= 0) { ClearBuff(p, name); return null; } }
            var h=p.health.hediffSet.GetFirstHediffOfDef(d)??p.health.AddHediff(d);
            var timer=h.TryGetComp<HediffComp_Disappears>();if(timer!=null)timer.ticksToDisappear=ticks; return h;
        }
        public static void ClearBuff(Pawn p,string name) { var h=p?.health?.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed(name));if(h!=null)p.health.RemoveHediff(h); }
        public static float Output(Pawn p) => Mathf.Clamp(p.GetStatValue(JJK_DefOf.JJK_CTOutput),.25f,5f);
        public static bool Hostile(Pawn p,Pawn q) => p!=null && q!=null && p!=q && (q.HostileTo(p)||p.HostileTo(q));
        public static void Hit(Pawn p,Pawn target,float total,DamageDef damage,float ap=.5f)
        {
            while(total>0 && target?.Spawned==true && !target.Dead) { float chunk=Mathf.Min(total,15);target.TakeDamage(new DamageInfo(damage,chunk,ap,-1,p));total-=chunk; }
        }
        public static void Heal(Pawn p,float pool,bool regrow=false)
        {
            foreach(var h in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) { float n=Mathf.Min(pool,h.Severity);h.Heal(n);pool-=n;if(pool<=0)break; }
            if(regrow && pool>0) { var missing=p.health.hediffSet.GetMissingPartsCommonAncestors().FirstOrDefault();if(missing!=null)p.health.RestorePart(missing.Part); }
        }
        public static Thing Supply(Pawn p,string def) => p.inventory?.innerContainer.FirstOrDefault(t=>t.def.defName==def)
            ?? p.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed(def)).FirstOrDefault(t=>!t.IsForbidden(p) && t.Position.DistanceToSquared(p.Position)<=2 && GenSight.LineOfSight(p.Position,t.Position,p.Map));
        public static bool Consume(Pawn p,string def) { var t=Supply(p,def);if(t==null)return false;t.SplitOff(1).Destroy();return true; }
        public static bool Place(Pawn p,string def)
        {
            var d=DefDatabase<ThingDef>.GetNamed(def);var t=ThingMaker.MakeThing(d,d.MadeFromStuff?ThingDefOf.Steel:null);
            if(p.inventory?.innerContainer.TryAdd(t)==true)return true;
            if(GenPlace.TryPlaceThing(t,p.Position,p.Map,ThingPlaceMode.Near))return true;t.Destroy();return false;
        }
        public static Pawn EnergyOwner(Pawn p) => p?.health?.hediffSet.hediffs.OfType<Hediff_ShikigamiBond>().FirstOrDefault()?.owner??p;
    }
    public class GameComponent_TechniqueExpansion : GameComponent
    {
        public List<TechniqueState> states=new List<TechniqueState>();
        public List<Projectile> reflected=new List<Projectile>();
        private readonly Dictionary<Pawn, List<TechniqueState>> starTargets = new Dictionary<Pawn, List<TechniqueState>>();
        private bool starTargetsDirty = true;
        internal void InvalidateStarTargets() => starTargetsDirty = true;
        internal List<TechniqueState> StarCastersFor(Pawn target)
        {
            if (starTargetsDirty)
            {
                foreach (var list in starTargets.Values) { list.Clear(); SimplePool<List<TechniqueState>>.Return(list); }
                starTargets.Clear();
                foreach (var state in states)
                    foreach (var mark in state.marks)
                    {
                        if (mark.target == null) continue;
                        if (!starTargets.TryGetValue(mark.target, out var list))
                            starTargets.Add(mark.target, list = SimplePool<List<TechniqueState>>.Get());
                        if (!list.Contains(state)) list.Add(state);
                    }
                starTargetsDirty = false;
            }
            // The usual case has no marks anywhere: avoid even walking the pawn's
            // hediffs to resolve a shikigami owner on every movement step.
            if (starTargets.Count == 0 || target == null) return null;
            target = ExpansionTech.EnergyOwner(target);
            return target != null && starTargets.TryGetValue(target, out var matches) ? matches : null;
        }
        public static GameComponent_TechniqueExpansion Instance => Current.Game?.GetComponent<GameComponent_TechniqueExpansion>();
        public GameComponent_TechniqueExpansion(Game game) { }
        public TechniqueState For(Pawn p) { var s=states.FirstOrDefault(x=>x.pawn==p);if(s==null){s=new TechniqueState{pawn=p};states.Add(s);}return s; }
        public override void GameComponentTick()
        {
            if(ExpansionTech.Now%30!=0)return;
            foreach(var s in states.ToList()) TickState(s);
            reflected.RemoveAll(p=>p==null||p.Destroyed);
        }
        public void TickState(TechniqueState s)
        {
            var p=s.pawn;int now=ExpansionTech.Now;
            if(p==null)return;
            bool recovering=s.Unsealed && s.Has("Pachinko") && s.jackpotUntil>now;
            if(recovering) {ExpansionTech.Buff(p,"JJK_Jackpot",45);var e=ExpansionTech.Energy(p);if(e!=null)e.energy=e.MaxEnergy;ExpansionTech.Heal(p,30,true);}
            if(!s.RikaActive)
            { s.RemoveLoan();if(s.rika!=null&&!s.rika.Destroyed)s.rika.Destroy();s.rika=null; }
            else
            {
                var imprint=s.copies.FirstOrDefault(c=>c.gene==s.selectedCopy);
                if(s.borrowed!=null && (imprint==null||!imprint.Valid))s.RemoveLoan();
                if(s.rika.Downed)ExpansionTech.Heal(s.rika,5);
                if(now%120==0)
                {
                    var enemy=p.Map.mapPawns.AllPawnsSpawned.Where(q=>!q.Dead&&!q.Downed&&ExpansionTech.Hostile(p,q)&&q.Position.DistanceToSquared(p.Position)<900)
                        .OrderBy(q=>q.Position.DistanceToSquared(s.rika.Position)).FirstOrDefault();
                    if(enemy!=null && s.rika.CurJob?.targetA.Pawn!=enemy)s.rika.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee,enemy));
                    else if(enemy==null && s.rika.Position.DistanceToSquared(p.Position)>25)s.rika.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,CellFinder.RandomClosewalkCellNear(p.Position,p.Map,2)));
                }
            }
            if(!s.Operational)
            {
                s.skyUntil=s.inverseUntil=s.gravityUntil=s.ritualUntil=0;StarTechnique.Clear(s);
                if(!recovering){s.jackpotUntil=0;ExpansionTech.ClearBuff(p,"JJK_Jackpot");}
                foreach(string h in new[]{"JJK_SkyGuard","JJK_InverseGuard","JJK_GravityGuard"})ExpansionTech.ClearBuff(p,h);
                return;
            }
            if(s.skyUntil>now && (!s.Has("SkyManipulation") || !ExpansionTech.Pay(p,1)))s.skyUntil=0;
            if(s.inverseUntil>now && (!s.Has("Inverse") || !ExpansionTech.Pay(p,1)))s.inverseUntil=0;
            if(s.gravityUntil>now && (!s.Has("Antigravity")||!ExpansionTech.Pay(p,2)))s.gravityUntil=0;
            if(s.skyUntil<=now)ExpansionTech.ClearBuff(p,"JJK_SkyGuard");
            if(s.inverseUntil<=now)ExpansionTech.ClearBuff(p,"JJK_InverseGuard");
            if(s.gravityUntil<=now)ExpansionTech.ClearBuff(p,"JJK_GravityGuard");
            if(s.ritualUntil>now)
            {
                if(!s.Has("SoloForbiddenArea") || p.Position!=s.ritualCell || p.pather.MovingNow || p.stances.stunner.Stunned || !ExpansionTech.Pay(p,2))s.ritualUntil=0;
                else foreach(var ally in p.Map.mapPawns.AllPawnsSpawned.Where(q=>!q.Dead&&!ExpansionTech.Hostile(p,q)&&q.Faction==p.Faction
                    && JJK_GradeUtility.IsSorcerer(q)&&q.Position.DistanceToSquared(p.Position)<=64&&GenSight.LineOfSight(p.Position,q.Position,p.Map)))
                { bool full=now-s.ritualStart>=300;
                    if(full){ExpansionTech.ClearBuff(ally,"JJK_RitualMinor");ExpansionTech.Buff(ally,"JJK_RitualFull",45);}
                    else if(!ally.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_RitualFull")))ExpansionTech.Buff(ally,"JJK_RitualMinor",45);
                }
            }
            if (s.marks.RemoveAll(m=>m.target==null||!m.target.Spawned||m.target.Map!=p.Map||m.target.Dead||m.until<=now) > 0) InvalidateStarTargets();
            if(!s.Has("LoveRendezvous"))StarTechnique.Clear(s);
            if(now%60==0)StarTechnique.Tick(s);
            if(!recovering){s.jackpotUntil=0;ExpansionTech.ClearBuff(p,"JJK_Jackpot");}
            var domain=p.Map.GetComponent<MapComponent_DomainManager>().DomainFor(p);
            if(domain?.Props?.gambling==true)TickGamble(s,domain);
        }
        public void TickGamble(TechniqueState s,ActiveDomain d)
        {
            int now=ExpansionTech.Now;
            if(s.gambleStart==0)
            {
                s.gambleSerial=now-d.ageTicks;s.gambleStart=now;s.gambleSpin=0;s.gambleWin=0;
                // Resolve the entire sequence at opening and save it. Reloading a
                // pending spin never rolls a different outcome.
                for(int i=1;i<=6;i++)if(Rand.Chance(.2f)){s.gambleWin=i;break;}
            }
            int spin=Mathf.Min(6,(now-s.gambleStart)/180);
            if(spin<=s.gambleSpin)return;s.gambleSpin=spin;
            Messages.Message("JJK_GambleSpin".Translate(s.pawn.LabelShortCap,spin),s.pawn,MessageTypeDefOf.NeutralEvent,false);
            if(s.gambleWin>0 && spin>=s.gambleWin)
            {
                s.pawn.Map.GetComponent<MapComponent_DomainManager>().CollapseFor(s.pawn,true);
                s.jackpotUntil=now+15060;s.gambleStart=0;ExpansionTech.Buff(s.pawn,"JJK_Jackpot",45);
                Messages.Message("JJK_JackpotWon".Translate(s.pawn.LabelShortCap),s.pawn,MessageTypeDefOf.PositiveEvent,false);
            }
            else if(spin==6) {s.pawn.Map.GetComponent<MapComponent_DomainManager>().CollapseFor(s.pawn,false);s.gambleStart=0;}
        }
        public override void ExposeData()
        {
            InvalidateStarTargets();
            if(Scribe.mode==LoadSaveMode.Saving) { states.RemoveAll(s=>s.pawn==null||s.pawn.Destroyed);reflected.RemoveAll(p=>p==null||p.Destroyed); }
            Scribe_Collections.Look(ref states,"techniqueExpansion",LookMode.Deep);Scribe_Collections.Look(ref reflected,"reflectedProjectiles",LookMode.Reference);
            if(Scribe.mode==LoadSaveMode.PostLoadInit) {states=states??new List<TechniqueState>();reflected=reflected??new List<Projectile>();states.RemoveAll(s=>s.pawn==null);}
        }
    }
}
