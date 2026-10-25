using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public enum ExpansionMove { Bullet,Blade,FireBullet,Metal,Armor,Sphere,SkyGuard,SkyDistort,ThinIce,Ritual,ReceiptKnives,ReceiptWeight,ReceiptRest,
        Inverse,StarCycle,StarMark,StarClear,Antigravity,Gravity,Doors,Rika,DismissRika,ConsumeCopy,ChooseCopy,Extinguish,Ladder,BreakBarrier }
    public class ExpansionMoveExtension : DefModExtension { public ExpansionMove move; public int mastery; }
    public class Ability_ExpansionMove : Ability
    {
        public ExpansionMove Move => def.GetModExtension<ExpansionMoveExtension>().move;
        public override Func<string> CanBeActivatedValidator() => ()=>Reason(false)??"";
        public string Reason(bool targetCheck)
        {
            if(pawn?.Spawned!=true||pawn.Dead||pawn.Downed)return "JJK_ExpansionUnavailable".Translate();
            var s=ExpansionTech.State(pawn);
            if(!s.Operational || !CopyTechnique.Allowed(this))return "JJK_ExpansionUnavailable".Translate();
            int xp=def.GetModExtension<ExpansionMoveExtension>().mastery;
            if(xp>GameComponent_JJK.Instance.GetTechniqueXp(pawn,def.abilityTrees[0]))return "JJK_ExpansionMastery".Translate(xp);
            if((Move==ExpansionMove.Bullet||Move==ExpansionMove.Blade)&&s.constructNext>ExpansionTech.Now)return "JJK_ConstructionRest".Translate();
            string supply=Move==ExpansionMove.FireBullet?"JJK_ConstructedBullet":Move==ExpansionMove.ReceiptKnives?"JJK_ReceiptKnives":Move==ExpansionMove.ReceiptWeight?"JJK_ReceiptWeight":Move==ExpansionMove.ReceiptRest?"JJK_ReceiptRest":null;
            if(supply!=null && ExpansionTech.Supply(pawn,supply)==null)return "JJK_NeedSupply".Translate(DefDatabase<ThingDef>.GetNamed(supply).LabelCap);
            if(Move==ExpansionMove.Gravity && pawn.story?.traits.HasTrait(JJK_DefOf.JJK_RCT)!=true)return "JJK_ReversalRequiresRCT".Translate();
            if(Move==ExpansionMove.Rika && (s.RikaActive||s.rikaCooldown>ExpansionTech.Now))return "JJK_RikaRest".Translate();
            if((Move==ExpansionMove.ConsumeCopy||Move==ExpansionMove.ChooseCopy)&&!s.RikaActive)return "JJK_RikaRequired".Translate();
            if(!targetCheck)return null;
            if(def.abilityTiers[0].targetingParameters!=null)
            {
                if(!curTarget.IsValid || !curTarget.Cell.InBounds(pawn.Map) || curTarget.Cell.DistanceToSquared(pawn.Position)>Mathf.Pow(def.abilityTiers[0].effectRadius+.1f,2)
                    || !GenSight.LineOfSight(pawn.Position,curTarget.Cell,pawn.Map))return "JJK_ExpansionTarget".Translate();
            }
            if(new[]{ExpansionMove.FireBullet,ExpansionMove.Metal,ExpansionMove.ThinIce,ExpansionMove.SkyDistort,ExpansionMove.Doors,ExpansionMove.Extinguish}.Contains(Move)
                && (curTarget.Pawn?.Spawned!=true || curTarget.Pawn.Map!=pawn.Map || curTarget.Pawn.Dead || !ExpansionTech.Hostile(pawn,curTarget.Pawn)))return "JJK_ExpansionTarget".Translate();
            if(Move==ExpansionMove.StarMark && (curTarget.Pawn?.Spawned!=true || ExpansionTech.Energy(ExpansionTech.EnergyOwner(curTarget.Pawn))==null))return "JJK_StarRequiresCE".Translate();
            if(Move==ExpansionMove.ConsumeCopy)return CopyTechnique.SampleReason(s,curTarget.Thing);
            if(Move==ExpansionMove.BreakBarrier && BarrierTarget()==null)return "JJK_NoBarrier".Translate();
            return null;
        }
        private ActiveDomain BarrierTarget() => pawn.Map.mapPawns.AllPawnsSpawned.Where(p=>ExpansionTech.Hostile(pawn,p))
            .Select(p=>pawn.Map.GetComponent<MapComponent_DomainManager>().DomainFor(p)).FirstOrDefault(d=>d!=null && d.Contains(pawn.Map,curTarget.Cell)
                && GameComponent_JJK.Instance.GetEffectiveMastery(pawn,def.abilityTrees[0])>=GameComponent_JJK.Instance.GetEffectiveMastery(d.caster,d.Tree)*.75f);
        public override void Start(bool consumeEnergy=true)
        {
            string reason=Reason(true);if(reason!=null){Messages.Message(reason,MessageTypeDefOf.RejectInput,false);return;}
            var s=ExpansionTech.State(pawn);int now=ExpansionTech.Now;var q=curTarget.Pawn;float output=ExpansionTech.Output(pawn);
            base.Start(consumeEnergy);
            switch(Move)
            {
                case ExpansionMove.Bullet: case ExpansionMove.Blade:
                    if(ExpansionTech.Place(pawn,Move==ExpansionMove.Bullet?"JJK_ConstructedBullet":"JJK_ConstructedBlade"))s.constructNext=now+60000;break;
                case ExpansionMove.FireBullet:
                    if(ExpansionTech.Consume(pawn,"JJK_ConstructedBullet"))Shoot(q,"JJK_Proj_ConstructedBullet",1);break;
                case ExpansionMove.Metal: ExpansionTech.Hit(pawn,q,45*output,DamageDefOf.Cut,1);ExpansionTech.Buff(q,"JJK_MetalBind",120);break;
                case ExpansionMove.Armor: ExpansionTech.Buff(pawn,"JJK_InsectArmor",1200);break;
                case ExpansionMove.Sphere:
                    pawn.Map.GetComponent<MapComponent_ExpansionBlasts>().Queue(pawn,curTarget.Cell,180,3,220*output,true);break;
                case ExpansionMove.SkyGuard: s.skyUntil=now+720;ExpansionTech.Buff(pawn,"JJK_SkyGuard",720);break;
                case ExpansionMove.SkyDistort: ExpansionTech.Buff(q,"JJK_SkyDistortion",360);break;
                case ExpansionMove.ThinIce: ExpansionTech.Hit(pawn,q,55*output,DamageDefOf.Blunt,1.5f);Shift(q,q.Position+(q.Position-pawn.Position).ClampMagnitude(1));break;
                case ExpansionMove.Ritual: s.ritualCell=pawn.Position;s.ritualStart=now;s.ritualUntil=now+1800;pawn.pather.StopDead();break;
                case ExpansionMove.ReceiptKnives: if(ExpansionTech.Consume(pawn,"JJK_ReceiptKnives"))Shoot(curTarget,"JJK_Proj_ReceiptKnife",3);break;
                case ExpansionMove.ReceiptWeight: if(ExpansionTech.Consume(pawn,"JJK_ReceiptWeight"))pawn.Map.GetComponent<MapComponent_ExpansionBlasts>().Queue(pawn,curTarget.Cell,120,3,50*output,false);break;
                case ExpansionMove.ReceiptRest: if(ExpansionTech.Consume(pawn,"JJK_ReceiptRest")&&pawn.needs?.rest!=null)pawn.needs.rest.CurLevel=Mathf.Min(1,pawn.needs.rest.CurLevel+.6f);break;
                case ExpansionMove.Inverse: s.inverseUntil=now+900;ExpansionTech.Buff(pawn,"JJK_InverseGuard",900);break;
                case ExpansionMove.StarCycle: s.nextStar=(s.nextStar+1)%5;Messages.Message("JJK_StarSelected".Translate(StarTechnique.Names[s.nextStar]),pawn,MessageTypeDefOf.NeutralEvent,false);break;
                case ExpansionMove.StarMark: StarTechnique.Mark(s,q);break;
                case ExpansionMove.StarClear: StarTechnique.Clear(s);break;
                case ExpansionMove.Antigravity: ExpansionTech.Buff(pawn,"JJK_Antigravity",900);ExpansionTech.ClearBuff(pawn,"JJK_GravityCrush");break;
                case ExpansionMove.Gravity:
                    s.gravityUntil=now+360;ExpansionTech.Buff(pawn,"JJK_GravityGuard",360);
                    foreach(var target in pawn.Map.mapPawns.AllPawnsSpawned.Where(p=>ExpansionTech.Hostile(pawn,p)&&p.Position.DistanceToSquared(pawn.Position)<=16
                        && GenSight.LineOfSight(pawn.Position,p.Position,pawn.Map)).ToList())
                    {if(target.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_Antigravity")))continue;
                        ExpansionTech.Hit(pawn,target,25*output,DamageDefOf.Blunt);ExpansionTech.Buff(target,"JJK_GravityCrush",360);target.stances.stunner.StunFor(90,pawn);}
                    break;
                case ExpansionMove.Doors: ExpansionTech.Hit(pawn,q,18*output,DamageDefOf.Blunt);q.stances.stunner.StunFor(90,pawn);break;
                case ExpansionMove.Rika: CopyTechnique.Manifest(s);break;
                case ExpansionMove.DismissRika: s.rikaUntil=0;GameComponent_TechniqueExpansion.Instance.TickState(s);break;
                case ExpansionMove.ConsumeCopy: Find.WindowStack.Add(new Window_CopyTechniques(s,curTarget.Thing));break;
                case ExpansionMove.ChooseCopy: Find.WindowStack.Add(new Window_CopyTechniques(s));break;
                case ExpansionMove.Extinguish: Seal(q,600);break;
                case ExpansionMove.Ladder:
                    foreach(var target in pawn.Map.mapPawns.AllPawnsSpawned.Where(p=>ExpansionTech.Hostile(pawn,p)&&p.Position.DistanceToSquared(curTarget.Cell)<=16
                        && GenSight.LineOfSight(curTarget.Cell,p.Position,pawn.Map)).ToList())
                    {Seal(target,900);ExpansionTech.Hit(pawn,target,(JJK_Utils.IsCursedSpirit(target)?100:35)*output,DamageDefOf.Burn,1.5f);}
                    CombatPresentation.Play("Healing",pawn.Map,curTarget.Cell);break;
                case ExpansionMove.BreakBarrier:
                    var domain=BarrierTarget();if(domain!=null)pawn.Map.GetComponent<MapComponent_DomainManager>().CollapseFor(domain.caster,false);break;
            }
        }
        public void Shoot(LocalTargetInfo target,string projectile,int count)
        { for(int i=0;i<count;i++)((Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed(projectile),pawn.Position,pawn.Map)).Launch(pawn,target,target,ProjectileHitFlags.IntendedTarget); }
        public static void Shift(Pawn p,IntVec3 cell)
        { if(p?.Spawned==true && MahoragaPhenomena.ControlFactor(p)>0f && cell.InBounds(p.Map)&&cell.Standable(p.Map)&&GenSight.LineOfSight(p.Position,cell,p.Map)&&!(cell.GetEdifice(p.Map) is DomainBarrierDoor)) {p.Position=cell;p.Notify_Teleported(false,true);} }
        private void Seal(Pawn p,int duration)
        {
            ExpansionTech.Buff(p,"JJK_TechniqueSealed",duration);DomainAmplificationUtil.EndInnateToggles(p);
        }
    }
    public class ExpansionBlast : IExposable
    {
        public Pawn caster;public IntVec3 cell;public int at;public float radius,damage;public bool sphere;
        public string phenomenon;
        public void ExposeData() { if(Scribe.mode==LoadSaveMode.Saving&&caster?.Destroyed==true)caster=null;Scribe_References.Look(ref caster,"caster");Scribe_Values.Look(ref cell,"cell");Scribe_Values.Look(ref at,"at");Scribe_Values.Look(ref radius,"radius");Scribe_Values.Look(ref damage,"damage");Scribe_Values.Look(ref sphere,"sphere"); Scribe_Values.Look(ref phenomenon,"phenomenon"); }
    }
    public class MapComponent_ExpansionBlasts : MapComponent
    {
        public List<ExpansionBlast> pending=new List<ExpansionBlast>();
        public MapComponent_ExpansionBlasts(Map map):base(map) { }
        public void Queue(Pawn p,IntVec3 c,int fuse,float radius,float damage,bool sphere) => pending.Add(new ExpansionBlast{caster=p,cell=c,at=ExpansionTech.Now+fuse,radius=radius,damage=damage,sphere=sphere,phenomenon=MahoragaExposure.ForSource(p) ?? (sphere ? "JJK_Adapt_Sphere" : "JJK_Adapt_ReceiptWeight")});
        public override void MapComponentTick()
        {
            if (pending.Count == 0) return;
            var due = SimplePool<List<ExpansionBlast>>.Get();
            try
            {
                // Capture due entries in queue order before damage can enqueue more.
                for (int i = 0; i < pending.Count; i++)
                    if (pending[i].at <= ExpansionTech.Now) due.Add(pending[i]);
                foreach (var b in due)
                {
                pending.Remove(b);
                using var exposure = new MahoragaExposure(b.phenomenon ?? (b.sphere ? "JJK_Adapt_Sphere" : "JJK_Adapt_ReceiptWeight"), b.caster);
                if(b.sphere)
                    foreach(var target in map.mapPawns.AllPawnsSpawned.Where(p=>!p.Dead&&p.Position.DistanceToSquared(b.cell)<=b.radius*b.radius&&GenSight.LineOfSight(b.cell,p.Position,map)).ToList())
                        ExpansionTech.Hit(b.caster,target,b.damage,DamageDefOf.Cut,100);
                else GenExplosion.DoExplosion(b.cell,map,b.radius,DamageDefOf.Bomb,b.caster,Mathf.RoundToInt(b.damage));
                }
            }
            finally { due.Clear(); SimplePool<List<ExpansionBlast>>.Return(due); }
        }
        public override void MapComponentUpdate() { foreach(var b in pending)CombatPresentation.Danger(map,GenRadial.RadialCellsAround(b.cell,b.radius,true).Where(c=>c.InBounds(map)).ToList()); }
        public override void MapComponentOnGUI() { foreach(var b in pending)CombatPresentation.Label(map,b.cell,"JJK_ExpansionBlast".Translate(b.sphere?"Perfect Sphere":"Falling weight",CombatPresentation.Countdown(b.at)),Color.white); }
        public override void ExposeData(){Scribe_Collections.Look(ref pending,"expansionBlasts",LookMode.Deep);if(Scribe.mode==LoadSaveMode.PostLoadInit)pending=pending??new List<ExpansionBlast>();}
    }
}
