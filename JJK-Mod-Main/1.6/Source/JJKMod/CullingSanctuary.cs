using HarmonyLib;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public static class CullingSanctuary
    {
        public static bool Protected(Thing t) => t?.Spawned == true && t.Map.Parent is CullingSite s
            && s.CampaignActive && !s.disrupted && GameComponent_CullingGame.Instance.data.protectedAreas
            && s.anchor?.Spawned == true && t.Position.InHorDistOf(s.anchor.Position, 6);
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_CullingSanctuary
    {
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (CullingSanctuary.Protected(__instance) || CullingSanctuary.Protected(dinfo.Instigator)) dinfo.SetAmount(0);
        }
    }
    public class MapComponent_CullingSanctuary : MapComponent
    {
        public MapComponent_CullingSanctuary(Map map) : base(map) { }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap == map && CombatPresentation.Overlays && map.Parent is CullingSite s && CullingSanctuary.Protected(s.anchor))
                GenDraw.DrawFieldEdges(CombatPresentation.VisibleCells(map, GenRadial.RadialCellsAround(s.anchor.Position, 6, true)), Color.green);
        }
        public override void MapComponentOnGUI()
        {
            if (map.Parent is CullingSite s && CullingSanctuary.Protected(s.anchor))
                CombatPresentation.Label(map, s.anchor.Position, "JJK_Sanctuary".Translate(), Color.green, -45);
        }
    }
}
