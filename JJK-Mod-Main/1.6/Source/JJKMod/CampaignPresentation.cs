using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public static class DomainInteriors
    {
        private class Visual
        {
            public Mesh floor, motif;
            public readonly HashSet<IntVec3> visible = new HashSet<IntVec3>();
            public readonly List<Matrix4x4[]> edges = new List<Matrix4x4[]>();
            public HashSet<IntVec3> interior;
            public int fogVersion = -1, cellCount;
            public Material edgeMaterial;
        }
        private class FogVersion { public int value; }
        private static ConditionalWeakTable<ActiveDomain, Visual> cache = new ConditionalWeakTable<ActiveDomain, Visual>();
        private static readonly ConditionalWeakTable<FogGrid, FogVersion> fogVersions = new ConditionalWeakTable<FogGrid, FogVersion>();
        private static readonly HashSet<Visual> liveVisuals = new HashSet<Visual>();

        internal static void FogChanged(FogGrid fog)
        {
            // No cache entry/allocation for maps without a drawn domain.
            if (fogVersions.TryGetValue(fog, out var version)) unchecked { version.value++; }
        }
        public static int Pattern(string name, int x, int z)
        {
            int ring = Mathf.FloorToInt(Mathf.Sqrt(x * x + z * z));
            if (name.Contains("Limitless")) return (x * x + z * z) % 17 == 0 ? 1 : 0;
            if (name.Contains("Flames")) return Math.Abs(x * 3 + z * 7) % 13 < 2 ? 1 : 0;
            if (name.Contains("Tides")) return ring % 4 == 0 ? 1 : 0;
            if (name.Contains("Plants")) return (Math.Abs(x) % 5 == 0 || (x + z * 2) % 9 == 0) ? 1 : 0;
            if (name.Contains("TenShadows")) return (x * x + 3 * z * z) % 23 < 7 ? 1 : 0;
            if (name.Contains("Shrine")) return Math.Abs(x) % 6 == 0 || Math.Abs(z) % 6 == 0 ? 1 : 0;
            if (name.Contains("Blood")) return Math.Abs(x - z) % 6 == 0 ? 1 : 0;
            if (name.Contains("Transfiguration")) return (Math.Abs(x) + Math.Abs(z)) % 5 == 0 ? 1 : 0;
            if (name.Contains("Projection")) return x % 3 == 0 && z % 3 == 0 ? 1 : 0;
            if (name.Contains("Ice")) return Math.Abs(x) == Math.Abs(z) || x % 7 == 0 || z % 7 == 0 ? 1 : 0;
            if (name.Contains("Gamble")) return Math.Abs(x) % 4 < 2 ? 1 : 0;
            return (Math.Abs(x) / 3 + Math.Abs(z) / 3) % 2;
        }
        private static Mesh MeshFor(IEnumerable<IntVec3> cells, float height)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (var c in cells)
            {
                int i = vertices.Count;
                vertices.Add(new Vector3(c.x, height, c.z)); vertices.Add(new Vector3(c.x, height, c.z + 1));
                vertices.Add(new Vector3(c.x + 1, height, c.z + 1)); vertices.Add(new Vector3(c.x + 1, height, c.z));
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
        public static void Draw(ActiveDomain d, Map map)
        {
            if (!CombatPresentation.Overlays || Find.CurrentMap != map) return;
            var v = Prepare(d, map);
            foreach (var batch in v.edges)
                Graphics.DrawMeshInstanced(MeshPool.plane10, 0, v.edgeMaterial, batch);
            if (d.Props.isSimpleDomain) return;
            Color tint = d.Props.barrierColor;
            Graphics.DrawMesh(v.floor, Vector3.zero, Quaternion.identity, SolidColorMaterials.SimpleSolidColorMaterial(new Color(tint.r * .2f, tint.g * .2f, tint.b * .2f, .35f)), 0);
            Graphics.DrawMesh(v.motif, Vector3.zero, Quaternion.identity, SolidColorMaterials.SimpleSolidColorMaterial(new Color(tint.r, tint.g, tint.b, .28f)), 0);
        }

        private static Visual Prepare(ActiveDomain d, Map map)
        {
            var v = cache.GetOrCreateValue(d);
            var interior = d.Interior(map);
            int version = fogVersions.GetOrCreateValue(map.fogGrid).value;
            if (v.interior != interior || v.cellCount != interior.Count || v.fogVersion != version)
            {
                liveVisuals.Add(v);
                if (v.floor != null) UnityEngine.Object.Destroy(v.floor);
                if (v.motif != null) UnityEngine.Object.Destroy(v.motif);
                v.visible.Clear();
                foreach (var cell in interior)
                    if (cell.InBounds(map) && !cell.Fogged(map)) v.visible.Add(cell);
                if (!d.Props.isSimpleDomain)
                {
                    float h = AltitudeLayer.Terrain.AltitudeFor() + .025f;
                    v.floor = MeshFor(v.visible, h);
                    v.motif = MeshFor(v.visible.Where(c => Pattern(d.sourceAbility.defName, c.x - d.center.x, c.z - d.center.z) != 0), h + .003f);
                }
                // Use vanilla field-edge texture, rotations, altitude and map-edge rule.
                // Cache the instance transforms instead of clearing/scanning a full map grid every frame.
                var color = d.Props.barrierColor;
                v.edgeMaterial = MaterialPool.MatFrom((Texture2D)GenDraw.TargetSquareMatSide.mainTexture, ShaderDatabase.Transparent, color);
                v.edgeMaterial.mainTexture.wrapMode = TextureWrapMode.Clamp;
                v.edgeMaterial.enableInstancing = true;
                float offset = Rand.ValueSeeded(color.ToOpaque().GetHashCode()) * .03658537f / 10f;
                v.edges.Clear();
                var matrices = new List<Matrix4x4>(1023);
                foreach (var cell in v.visible)
                    for (int i = 0; i < 4; i++)
                    {
                        var next = cell + new Rot4(i).FacingCell;
                        if (!next.InBounds(map) || v.visible.Contains(next)) continue;
                        matrices.Add(Matrix4x4.TRS(cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays)
                            + new Vector3(0, offset, 0), new Rot4(i).AsQuat, Vector3.one));
                        if (matrices.Count == 1023) { v.edges.Add(matrices.ToArray()); matrices.Clear(); }
                    }
                if (matrices.Count > 0) v.edges.Add(matrices.ToArray());
                v.interior = interior; v.cellCount = interior.Count; v.fogVersion = version;
            }
            return v;
        }
        internal static bool CachedCellVisible(ActiveDomain d, Map map, IntVec3 cell) => Prepare(d, map).visible.Contains(cell);
        internal static Mesh CachedFloor(ActiveDomain d, Map map) => Prepare(d, map).floor;
        public static void Release(ActiveDomain d)
        {
            if (!cache.TryGetValue(d, out var v)) return;
            if (v.floor != null) UnityEngine.Object.Destroy(v.floor);
            if (v.motif != null) UnityEngine.Object.Destroy(v.motif);
            liveVisuals.Remove(v);
            cache.Remove(d);
        }
        internal static void Reset()
        {
            foreach (var v in liveVisuals)
            {
                if (v.floor != null) UnityEngine.Object.Destroy(v.floor);
                if (v.motif != null) UnityEngine.Object.Destroy(v.motif);
            }
            liveVisuals.Clear(); cache = new ConditionalWeakTable<ActiveDomain, Visual>();
        }
    }

    // Invalidate before the next render, including fog changes while paused.
    [HarmonyPatch(typeof(FogGrid), "UnfogWorker")]
    public static class Patch_DomainUnfog
    {
        public static void Prefix(FogGrid __instance, IntVec3 __0, out bool __state) => __state = __instance.IsFogged(__0);
        public static void Postfix(FogGrid __instance, bool __state) { if (__state) DomainInteriors.FogChanged(__instance); }
    }
    [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Refog))]
    public static class Patch_DomainRefog
    { public static void Postfix(FogGrid __instance) => DomainInteriors.FogChanged(__instance); }
    [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.SetAllFogged))]
    public static class Patch_DomainAllFogged
    { public static void Postfix(FogGrid __instance) => DomainInteriors.FogChanged(__instance); }

    public class GameComponent_DomainVisualLifetime : GameComponent
    {
        public GameComponent_DomainVisualLifetime(Game game) { DomainInteriors.Reset(); }
    }
    public static class TechniquePreview
    {
        public static Ability selected;
        public static List<IntVec3> Cells(Ability a, LocalTargetInfo target, out string key)
        {
            key = "JJK_PreviewTarget";
            if (a?.pawn?.Spawned != true || !target.IsValid) return new List<IntVec3>();
            var domain = a.def.GetModExtension<DomainExtension>();
            if (domain != null) return DomainGeometry.ComputeInterior(a.pawn.Map, a.pawn.Position,
                domain.radius * (GameComponent_SorceryDevelopment.Instance?.For(a.pawn).Refinement(a.def.abilityTrees?.FirstOrDefault()) == DomainRefinement.Condensed ? .65f : 1f)).ToList();
            float radius = 0;
            if (a is Ability_LegacyTechnique legacy)
            {
                radius = legacy.Props.radius;
                if (legacy.Props.move == LegacyMove.Supernova && a.pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_Convergence"))) radius = 4f;
                if (legacy.Props.move == LegacyMove.LavaLance || legacy.Props.move == LegacyMove.PiercingBlood)
                {
                    key = "JJK_PreviewLine";
                    return LegacyTechniqueUtility.LineCells(a.pawn, target.Cell);
                }
            }
            else if (a is Ability_LapseBlue) { radius = 3.5f; key = "JJK_PreviewPull"; }
            else if (a is Ability_ReversalRed) radius = 5.9f;
            else if (a is Ability_SpeechExplode) radius = 2.4f;
            else if (a is Ability_Collapse) radius = 4.5f;
            else if (a is Ability_MaximumUzumaki) radius = Mathf.Min(2.5f + GameComponent_JJK.Instance.GetAbsorbedCurses(a.pawn).SelectedCount * .35f, 8);
            else if (a is Ability_PuppetOrder && a.def.GetModExtension<PuppetAbilityExtension>()?.move == PuppetMove.UltimateCannon) radius = 2.9f;
            else if (a is Ability_HollowPurple)
            {
                key = "JJK_PreviewLine";
                var cells = new List<IntVec3>(); var dir = (target.Cell - a.pawn.Position).ToVector3().normalized;
                if (dir == Vector3.zero) return cells;
                for (int i = 1; i <= 40; i++)
                {
                    var cell = (a.pawn.Position.ToVector3Shifted() + dir * i).ToIntVec3();
                    if (!cell.InBounds(a.pawn.Map)) break;
                    if (cell != a.pawn.Position && !cells.Contains(cell)) cells.Add(cell);
                }
                return cells;
            }
            else radius = a.AbilityTier?.verbProperties?.FirstOrDefault()?.defaultProjectile?.projectile?.explosionRadius ?? 0;
            if (radius > 0)
            {
                if (key != "JJK_PreviewPull") key = "JJK_PreviewArea";
                return GenRadial.RadialCellsAround(target.Cell, radius, true).Where(c => c.InBounds(a.pawn.Map)).ToList();
            }
            return new List<IntVec3> { target.Cell };
        }
        public static void Draw(Ability a, LocalTargetInfo target)
        {
            if (a?.pawn?.Spawned != true || !CombatPresentation.Overlays || Find.CurrentMap != a.pawn.Map) return;
            var cells = Cells(a, target, out _);
            GenDraw.DrawFieldEdges(CombatPresentation.VisibleCells(a.pawn.Map, cells), a.CanHitTarget(target) ? Color.cyan : Color.red);
            if (a is Ability_PuppetOrder order && target.Pawn != null)
            {
                var source = order.Source(target.Pawn);
                if (source != null) GenDraw.DrawLineBetween(source.Unit.DrawPos, target.Pawn.DrawPos, SimpleColor.Cyan);
            }
        }
    }
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetCommandActivateAbility))]
    public static class Patch_TechniquePreviewCommand
    {
        public static void Postfix(Ability __instance, Command_ActivateAbility __result)
        {
            if (__result == null || !__instance.def.defName.StartsWith("JJK_")) return;
            var original = __result.action;
            __result.action = () => { TechniquePreview.selected = __instance; original?.Invoke(); };
            var hover = __result.onHover;
            __result.onHover = () =>
            {
                hover?.Invoke();
                if (__instance.def.GetModExtension<DomainExtension>() != null && __instance.pawn?.Spawned == true)
                    TechniquePreview.Draw(__instance, __instance.pawn);
            };
        }
    }
    public class MapComponent_TechniquePreview : MapComponent
    {
        public MapComponent_TechniquePreview(Map map) : base(map) { }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            if (!Find.Targeter.IsTargeting) { TechniquePreview.selected = null; return; }
            IntVec3 cell = UI.MouseCell();
            if (TechniquePreview.selected?.pawn?.Map == map && cell.InBounds(map))
                TechniquePreview.Draw(TechniquePreview.selected, cell.GetFirstPawn(map) is Pawn p ? new LocalTargetInfo(p) : new LocalTargetInfo(cell));
        }
        public override void MapComponentOnGUI()
        {
            var a = TechniquePreview.selected;
            if (a?.pawn?.Map != map || !Find.Targeter.IsTargeting) return;
            TechniquePreview.Cells(a, UI.MouseCell(), out var key);
            CombatPresentation.Label(map, UI.MouseCell(), key.Translate(), Color.white, -25);
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TechniqueImpact
    {
        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result)
        {
            if (!(__instance is Pawn victim) || __result == null || __result.totalDamageDealt < 8 || !CombatPresentation.Overlays
                || !(dinfo.Instigator is Pawn caster) || !JJK_GradeUtility.IsSorcerer(caster)
                || !CombatPresentation.Visible(victim.Map, victim.Position)) return;
            if (!victim.Map.GetComponent<MapComponent_CombatAudio>().Accept("impact-" + victim.ThingID)) return;
            FleckMaker.ThrowMicroSparks(victim.DrawPos, victim.Map);
            FleckMaker.Static(victim.Position, victim.Map, FleckDefOf.PsycastAreaEffect, Mathf.Min(1.5f, __result.totalDamageDealt / 20f));
        }
    }
}
