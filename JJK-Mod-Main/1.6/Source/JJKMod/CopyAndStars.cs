using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public static class CopyTechnique
    {
        public static IEnumerable<GeneDef> Candidates(Pawn p) => JJK_Utils.CursedTechniqueGenes.Where(g=>g.defName!="JJK_CT_Gene_Copy"
            && p?.genes?.HasActiveGene(g)==true);
        public static Pawn Subject(Thing thing) => thing as Pawn ?? (thing as Corpse)?.InnerPawn;
        public static BodyPartRecord SamplePart(Pawn p) => p?.health?.hediffSet.GetNotMissingParts().FirstOrDefault(b=>b.def.defName=="Arm")
            ?? p?.health?.hediffSet.GetNotMissingParts().FirstOrDefault(b=>b.def.defName=="Hand");
        public static string SampleReason(TechniqueState s,Thing thing)
        {
            var p=Subject(thing);
            if(!s.RikaActive || thing?.Spawned!=true || thing.Map!=s.pawn.Map || p?.RaceProps.Humanlike!=true
                || p==s.pawn || p.Faction==s.pawn.Faction || (p.Faction!=null && s.pawn.Faction!=null && p.Faction.RelationKindWith(s.pawn.Faction)==FactionRelationKind.Ally)
                || (!p.Dead && (!p.Downed||!ExpansionTech.Hostile(s.pawn,p)))
                || s.rika.Position.DistanceToSquared(thing.Position)>25 || s.pawn.Position.DistanceToSquared(thing.Position)>64
                || SamplePart(p)==null || !Candidates(p).Any())return "JJK_CopySampleInvalid".Translate();
            if(s.copies.Count>=3)return "JJK_CopyFull".Translate();return null;
        }
        public static bool Sample(TechniqueState s,Thing thing,GeneDef gene)
        {
            if(SampleReason(s,thing)!=null || !Candidates(Subject(thing)).Contains(gene) || s.copies.Any(c=>c.gene==gene) || s.pawn.genes.HasActiveGene(gene))return false;
            var p=Subject(thing);var part=SamplePart(p);
            // Actual consumption. A later restoration of this exact part invalidates
            // a living donor's imprint. No permanent innate gene is transferred.
            p.health.AddHediff(HediffDefOf.MissingBodyPart,part);
            s.copies.Add(new CopyImprint {gene=gene,source=p,partIndex=p.RaceProps.body.AllParts.IndexOf(part),sourceDead=p.Dead});
            s.selectedCopy=gene;return true;
        }
        public static void Manifest(TechniqueState s)
        {
            s.rika=JJK_SummonUtility.SummonShadow(s.pawn,DefDatabase<PawnKindDef>.GetNamed("JJK_Rika_Kind"),18000,false,DefDatabase<ShikigamiDef>.GetNamed("JJK_Shadow_Rika"));
            s.rikaUntil=ExpansionTech.Now+18000;s.rikaCooldown=ExpansionTech.Now+60000;
            if(s.selectedCopy!=null)s.SelectCopy(s.selectedCopy);
        }
        public static bool IsLoan(Ability a,TechniqueState s) => s?.borrowed?.GetModExtension<AbilityExtension>()?.abilityTrees?.Any(t=>a.def.abilityTrees?.Contains(t)==true)==true;
        public static bool Allowed(Ability a)
        {
            var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(x=>x.pawn==a?.pawn);
            if(s==null)return true;
            if(!IsLoan(a,s))
                return !s.Has("Copy") || !JJK_Utils.CursedTechniqueGenes.Any(g=>!a.pawn.genes.HasActiveGene(g)&&g.GetModExtension<AbilityExtension>()?.abilityTrees?.Any(t=>a.def.abilityTrees?.Contains(t)==true)==true);
            return s.RikaActive && a.def.GetModExtension<DomainExtension>()==null && s.copies.Any(c=>c.gene==s.selectedCopy&&c.Valid);
        }
        public static void RecordCast(Ability a)
        {
            var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(x=>x.pawn==a.pawn);
            if(s==null||!IsLoan(a,s))return;
            var copy=s.copies.FirstOrDefault(c=>c.gene==s.selectedCopy&&c.Valid);
            if(copy!=null){copy.uses--;copy.cooldowns[a.def]=ExpansionTech.Now+a.cooldownPeriod;}
        }
    }
    public class Window_CopyTechniques : Window
    {
        private readonly TechniqueState state;private readonly Thing sample;private Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(650,570);
        public Window_CopyTechniques(TechniqueState state,Thing sample=null) {this.state=state;this.sample=sample;doCloseX=true;forcePause=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect rect)
        {
            var list=new Listing_Standard();var view=new Rect(0,0,rect.width-20,250+state.copies.Count*125);
            Widgets.BeginScrollView(rect,ref scroll,view);list.Begin(view);
            list.Label(sample!=null?"JJK_CopyConsumeHelp".Translate():"JJK_CopyHelp".Translate());list.GapLine();
            if(sample!=null)
            {
                var reason=CopyTechnique.SampleReason(state,sample);
                if(reason!=null)list.Label(reason);
                else foreach(var gene in CopyTechnique.Candidates(CopyTechnique.Subject(sample)).ToList())
                    if(!state.pawn.genes.HasActiveGene(gene)&&!state.copies.Any(c=>c.gene==gene)&&list.ButtonText(gene.LabelCap))
                    {if(CopyTechnique.Sample(state,sample,gene))state.SelectCopy(gene);Close();break;}
            }
            else foreach(var copy in state.copies.ToList())
            {
                list.Label(copy.gene.LabelCap+" — "+"JJK_CopyUses".Translate(copy.uses)+(copy.Valid?"":" ("+"JJK_CopyInvalid".Translate().ToString()+")"));
                if(copy.Valid && list.ButtonText("JJK_CopySelect".Translate(copy.gene.LabelCap)))state.SelectCopy(copy.gene);
                if(list.ButtonText("JJK_CopyForget".Translate(copy.gene.LabelCap)))
                {if(state.selectedCopy==copy.gene){state.RemoveLoan();state.selectedCopy=null;}state.copies.Remove(copy);}
                list.GapLine();
            }
            list.End();Widgets.EndScrollView();
        }
    }
    public static class StarTechnique
    {
        public static readonly string[] Names={"Imai","Acrux","Mimosa","Ginan","Gacrux"};
        public static void Mark(TechniqueState s,Pawn target)
        {
            target=ExpansionTech.EnergyOwner(target);s.marks.RemoveAll(m=>m.target==target);
            s.marks.Add(new StarMark{target=target,star=s.nextStar,progress=s.nextStar,until=ExpansionTech.Now+1800});
            GameComponent_TechniqueExpansion.Instance.InvalidateStarTargets();
            Messages.Message("JJK_StarMarked".Translate(target.LabelShortCap,Names[s.nextStar]),target,MessageTypeDefOf.NeutralEvent,false);
        }
        public static void Clear(TechniqueState s)
        {
            if (s.marks.Count == 0) return;
            s.marks.Clear(); GameComponent_TechniqueExpansion.Instance.InvalidateStarTargets();
        }
        public static bool MayMove(Pawn pawn,IntVec3 next)
        {
            var gc=GameComponent_TechniqueExpansion.Instance;if(gc==null)return true;
            var candidates = gc.StarCastersFor(pawn);
            if (candidates == null) return true;
            var owner = ExpansionTech.EnergyOwner(pawn);
            foreach(var s in candidates)
            {
                // The index narrows candidates only; all dynamic eligibility remains live.
                if (!gc.states.Contains(s) || !s.Operational || !s.Has("LoveRendezvous") || s.pawn.Map != pawn.Map) continue;
                var own=s.marks.FirstOrDefault(m=>m.target==owner&&m.until>ExpansionTech.Now);if(own==null)continue;
                foreach(var other in s.marks.Where(m=>m.target?.Spawned==true&&m.target!=owner&&m.until>ExpansionTech.Now&&m.star>own.progress+1))
                    if(next.DistanceToSquared(other.target.Position)<36 && next.DistanceToSquared(other.target.Position)<pawn.Position.DistanceToSquared(other.target.Position)
                        && MahoragaPhenomena.ControlFactor(pawn, "JJK_Adapt_Stars") > 0f)return false;
            }
            return true;
        }
        public static void Tick(TechniqueState s)
        {
            foreach(var mark in s.marks)
            {
                var p=mark.target;if(p?.Spawned!=true)continue;
                var next=s.marks.FirstOrDefault(m=>m.star==mark.progress+1&&m.target!=p&&m.target.Position.DistanceToSquared(p.Position)<=9);
                if(next!=null)mark.progress=next.star;
                var same=s.marks.Where(m=>m.target!=p&&m.star==mark.star&&m.target.Position.DistanceToSquared(p.Position)>2
                    && m.target.Position.DistanceToSquared(p.Position)<=144).OrderBy(m=>m.target.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                if(same==null || ExpansionTech.Energy(p)?.MaxEnergy>ExpansionTech.Energy(same.target)?.MaxEnergy)continue;
                IntVec3 delta=same.target.Position-p.Position;var cell=p.Position+new IntVec3(Math.Sign(delta.x),0,Math.Sign(delta.z));
                if(GenSight.LineOfSight(p.Position,cell,p.Map))
                    using (new MahoragaExposure("JJK_Adapt_Stars", s.pawn)) Ability_ExpansionMove.Shift(p,cell);
            }
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower),"TryEnterNextPathCell")]
    public static class Patch_StarMovement
    {
        public static bool Prefix(Pawn_PathFollower __instance,Pawn ___pawn,IntVec3 ___nextCell)
        {if(StarTechnique.MayMove(___pawn,___nextCell))return true;__instance.StopDead();return false;}
    }
    [HarmonyPatch(typeof(Ability),nameof(Ability.Start))]
    public static class Patch_CopyUses
    { public static void Postfix(Ability __instance) { CopyTechnique.RecordCast(__instance); } }
    [HarmonyPatch(typeof(Ability),"CanBeActivated")]
    public static class Patch_CopyAvailable
    {public static void Postfix(Ability __instance,ref bool __result) {if(!CopyTechnique.Allowed(__instance))__result=false;} }
}
