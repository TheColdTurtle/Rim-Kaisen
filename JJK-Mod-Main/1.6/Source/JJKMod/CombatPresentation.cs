using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Presentation only: cues never decide damage, protection, targeting or timing.
    public static class CombatPresentation
    {
        public static bool Sounds => RimKaisenMod.Settings?.combatSounds ?? true;
        public static bool Overlays => RimKaisenMod.Settings?.combatOverlays ?? true;
        public static bool Visible(Map map, IntVec3 cell) => map != null && Find.CurrentMap == map
            && cell.InBounds(map) && !cell.Fogged(map);

        public static string CastCue(AbilityDef def)
        {
            if (def == null || !def.defName.StartsWith("JJK_") || def.GetModExtension<DomainExtension>() != null) return null;
            string tree = def.abilityTrees?.FirstOrDefault()?.defName;
            // The user explicitly requested a completely clean Boogie Woogie swap.
            switch (tree)
            {
                case "JJK_CT_BoogieWoogie": return null;
                case "JJK_CT_CursedSpeech": return "Speech";
                case "JJK_CT_StrawDoll": return "Nails";
                case "JJK_CT_DisasterFlames": return "Fire";
                case "JJK_CT_IceFormation": return "Ice";
                case "JJK_CT_RCT": return "Healing";
                case "JJK_CT_Ratio": case "JJK_CT_Shrine": case "JJK_CT_NewShadowStyle": return "Slash";
                case "JJK_CT_TenShadows": case "JJK_CT_SpiritManipulation": case "JJK_CT_IdleTransfiguration":
                case "JJK_CT_AuspiciousBeasts": case "JJK_CT_BlackBird": return "Spirit";
                case "JJK_CT_PuppetManipulation": return null; // actual puppet, not its distant operator
                default: return "Energy";
            }
        }
        public static void Cast(TaranMagicFramework.Ability ability)
        {
            var p = ability?.pawn;
            if (p?.Spawned == true) Play(CastCue(ability.def), p.Map, p.Position);
        }
        public static bool Play(string cue, Map map, IntVec3 cell)
        {
            if (cue == null || !Sounds || !Visible(map, cell)) return false;
            var sound = DefDatabase<SoundDef>.GetNamedSilentFail("JJK_Cue_" + cue);
            if (sound == null || !map.GetComponent<MapComponent_CombatAudio>().Accept(cue)) return false;
            sound.PlayOneShot(SoundInfo.InMap(new TargetInfo(cell, map)));
            return true;
        }
        public static string Countdown(int triggerTick) => (Mathf.Max(0, triggerTick - Find.TickManager.TicksGame) / 60f).ToString("0.0") + "s";
        public static List<IntVec3> VisibleCells(Map map, IEnumerable<IntVec3> cells) => cells.Where(c => c.InBounds(map) && !c.Fogged(map)).ToList();
        public static void Danger(Map map, List<IntVec3> cells)
        {
            if (!Overlays || Find.CurrentMap != map || cells.Count == 0) return;
            // Static hatching stays legible while paused; no flashing/strobing.
            var visible = VisibleCells(map, cells);
            GenDraw.DrawDiagonalStripes(visible, new Color(1f, 0.35f, 0.08f, 0.3f));
            GenDraw.DrawFieldEdges(visible, new Color(1f, 0.55f, 0.12f));
        }
        public static void Label(Map map, IntVec3 cell, string text, Color color, float offset = 0)
        {
            if (!Overlays || !Visible(map, cell) || Find.CameraDriver.CurrentZoom == CameraZoomRange.Furthest) return;
            var pos = GenMapUI.LabelDrawPosFor(cell); pos.y += offset;
            GenMapUI.DrawThingLabel(pos, text, color);
        }
        public static string DomainLabel(ActiveDomain d)
        {
            if (d.Props.isSimpleDomain)
                return "JJK_OverlayShelter".Translate(d.sourceAbility.LabelCap, Mathf.CeilToInt(d.integrity));
            return (d.Props.closed ? "JJK_OverlayClosed" : "JJK_OverlayOpen").Translate(d.sourceAbility.LabelCap);
        }
    }
    // Transient, per-map throttling: a volley/group cast cannot produce dozens of identical voices.
    public class MapComponent_CombatAudio : MapComponent
    {
        private readonly Dictionary<string, int> last = new Dictionary<string, int>();
        public MapComponent_CombatAudio(Map map) : base(map) { }
        public bool Accept(string cue)
        {
            int now = Find.TickManager.TicksGame;
            if (last.TryGetValue(cue, out int previous) && now >= previous && now - previous < 8) return false;
            last[cue] = now; return true;
        }
    }
}
