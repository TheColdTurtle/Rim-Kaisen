using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public class HediffCompProperties_DomainActive : HediffCompProperties
    {
        public HediffCompProperties_DomainActive()
        {
            compClass = typeof(HediffComp_DomainActive);
        }
    }

    // Carried by the caster while their Domain Expansion is open. Provides the
    // voluntary "Collapse Domain" gizmo (a willing dismissal costs less burnout
    // than letting it run dry). The buff itself comes from the hediff's stage
    // statFactors; the manager toggles severity by proximity.
    public class HediffComp_DomainActive : HediffComp
    {
        private MapComponent_DomainManager Manager =>
            Pawn?.Map?.GetComponent<MapComponent_DomainManager>();

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Faction == null || !pawn.Faction.IsPlayer) yield break;

            // Simple Domain is exempt from burnout entirely (see MapComponent_DomainManager
            // .Collapse), so it must not show the shared 6h/24h burnout warning.
            bool simple = Manager?.DomainFor(pawn)?.Props?.isSimpleDomain ?? false;

            yield return new Command_Action
            {
                defaultLabel = "JJK_CollapseDomain".Translate(),
                defaultDesc = (simple ? "JJK_CollapseSimpleDomainDesc" : "JJK_CollapseDomainDesc").Translate(),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => Manager?.CollapseFor(pawn, voluntary: true)
            };
        }

        public override string CompTipStringExtra
        {
            get
            {
                ActiveDomain d = Manager?.DomainFor(Pawn);
                if (d == null) return null;
                DomainExtension props = d.Props;
                // Simple Domain: surface its remaining durability instead.
                if (props != null && props.isSimpleDomain)
                {
                    float pct = props.simpleDomainIntegrity > 0f
                        ? d.integrity / props.simpleDomainIntegrity
                        : 0f;
                    return "JJK_SimpleDomainIntegrity".Translate(Mathf.Clamp01(pct).ToStringPercent());
                }
                bool inside = Pawn.Position.InHorDistOf(d.center, props?.radius ?? 15f);
                return inside
                    ? "JJK_DomainInside".Translate()
                    : "JJK_DomainOutside".Translate();
            }
        }
    }
}
