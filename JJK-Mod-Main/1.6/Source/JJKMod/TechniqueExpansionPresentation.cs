using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public class Ability_GambleDomain : Ability_DomainExpansion
    {
        public override Func<string> CanBeActivatedValidator()=>()=>Blocked()?"JJK_GambleActive".Translate().ToString():"";
        private bool Blocked()=>pawn?.Spawned!=true || !ExpansionTech.State(pawn).Operational
            || ExpansionTech.State(pawn).jackpotUntil>ExpansionTech.Now || pawn.Map.GetComponent<MapComponent_DomainManager>().HasDomain(pawn);
        public override void Start(bool consumeEnergy=true){if(!Blocked())base.Start(consumeEnergy);}
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    public static class Patch_ExpansionGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,Pawn __instance)
        {
            foreach(var g in __result)yield return g;
            var s=GameComponent_TechniqueExpansion.Instance?.states.FirstOrDefault(s=>s.pawn==__instance);
            if(s==null||__instance.Faction!=Faction.OfPlayer)yield break;
            int now=ExpansionTech.Now;
            if(s.skyUntil>now||s.inverseUntil>now||s.gravityUntil>now||s.ritualUntil>now)
                yield return new Command_Action {defaultLabel="JJK_EndExpansionDefense".Translate(),defaultDesc="JJK_EndExpansionDefense".Translate(),
                    icon=TexCommand.ClearPrioritizedWork,action=()=>{s.skyUntil=s.inverseUntil=s.gravityUntil=s.ritualUntil=0;GameComponent_TechniqueExpansion.Instance.TickState(s);}};
            if(s.pawn.Map?.GetComponent<MapComponent_DomainManager>().DomainFor(s.pawn)?.Props?.gambling==true)
                yield return new Command_Action {defaultLabel="End Idle Death Gamble",defaultDesc="Abandon the current spins. Failed gambling causes burnout.",
                    icon=TexCommand.ClearPrioritizedWork,action=()=>s.pawn.Map.GetComponent<MapComponent_DomainManager>().CollapseFor(s.pawn,false)};
        }
    }
    public class MapComponent_ExpansionPresentation : MapComponent
    {
        public MapComponent_ExpansionPresentation(Map map):base(map){}
        public override void MapComponentOnGUI()
        {
            var gc=GameComponent_TechniqueExpansion.Instance;if(gc==null)return;
            foreach(var s in gc.states.Where(s=>s.pawn?.Spawned==true&&s.pawn.Map==map))
            {
                if(s.jackpotUntil>ExpansionTech.Now)CombatPresentation.Label(map,s.pawn.Position,"Jackpot "+CombatPresentation.Countdown(s.jackpotUntil),Color.yellow);
                else if(s.ritualUntil>ExpansionTech.Now)CombatPresentation.Label(map,s.pawn.Position,"Ritual "+CombatPresentation.Countdown(s.ritualUntil),Color.cyan);
                else if(map.GetComponent<MapComponent_DomainManager>().DomainFor(s.pawn)?.Props?.gambling==true)
                    CombatPresentation.Label(map,s.pawn.Position,"Spin "+s.gambleSpin+" / 6",Color.yellow);
                if(Find.Selector.IsSelected(s.pawn))
                    foreach(var m in s.marks.Where(m=>m.target?.Spawned==true && m.until>ExpansionTech.Now))
                        CombatPresentation.Label(map,m.target.Position,StarTechnique.Names[m.star]+" → "+StarTechnique.Names[Math.Min(4,m.progress+1)]+" "+CombatPresentation.Countdown(m.until),Color.magenta);
            }
        }
    }
}
