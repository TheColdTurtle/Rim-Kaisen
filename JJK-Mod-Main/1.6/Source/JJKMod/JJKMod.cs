using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public class RimKaisenMod : Mod
    {
        private static RimKaisenMod instance;
        public static JJKModSettings Settings => instance?.GetSettings<JJKModSettings>();
        private Vector2 settingsScroll;
        private float settingsHeight = 1600f;

        public RimKaisenMod(ModContentPack content) : base(content)
        {
            instance = this;
            GetSettings<JJKModSettings>();
            var harmony = new Harmony("JJKMod.RimKaisen");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override string SettingsCategory() => "Rim-Kaisen";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.Validate();
            Rect footer = new Rect(inRect.x, inRect.yMax - 34f, inRect.width, 30f);
            Rect outer = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 44f);
            Rect view = new Rect(0f, 0f, outer.width - 24f, settingsHeight);
            Widgets.BeginScrollView(outer, ref settingsScroll, view);
            var ls = new Listing_Standard();
            // Listing_Standard starts a new column when it reaches its rect height.
            // Use an ample layout rect so revealing conditional rows cannot wrap
            // into invisible columns before the measured scroll height catches up.
            ls.Begin(new Rect(0f, 0f, view.width, 100000f));
            ls.Label("JJK_Settings_Overview".Translate());

            Section(ls, "Progression");
            Toggle(ls, "RandomGenesOnPawnGen", ref Settings.randomCursedGenesOnPawnGen);
            if (Settings.randomCursedGenesOnPawnGen)
                Slider(ls, "NaturalGeneChance", ref Settings.naturalGeneChance, 0f, 0.28f, 0.005f);
            Slider(ls, "GradeXpFactor", ref Settings.gradeXpFactor, 0.25f, 4f, 0.05f);
            Slider(ls, "MasteryXpFactor", ref Settings.masteryXpFactor, 0.25f, 4f, 0.05f);
            Slider(ls, "RCTAwakeningFactor", ref Settings.rctAwakeningFactor, 0f, 4f, 0.05f);

            Section(ls, "Combat");
            Toggle(ls, "MoodScalesCE", ref Settings.moodScalesCERegen);
            Toggle(ls, "EnemyDomains", ref Settings.enemyAIDomains);
            Slider(ls, "BlackFlashChanceFactor", ref Settings.blackFlashChanceFactor, 0f, 4f, 0.05f);

            Section(ls, "Curses");
            Toggle(ls, "Manifestations", ref Settings.curseManifestations);
            Toggle(ls, "DisasterIncidents", ref Settings.disasterCurseIncidents);
            Toggle(ls, "CorpseCurses", ref Settings.corpseCurses);
            if (Settings.corpseCurses)
            {
                Slider(ls, "CorpseCurseChanceFactor", ref Settings.corpseCurseChanceFactor, 0.25f, 4f, 0.05f);
                Slider(ls, "CorpseCurseThreshold", ref Settings.corpseCurseThreshold, 1f, 40f, 1f, false);
                Slider(ls, "CorpseCurseCooldownDays", ref Settings.corpseCurseCooldownDays, 0.125f, 3f, 0.125f, false);
            }
            Slider(ls, "CurseThreatFactor", ref Settings.curseThreatFactor, 0.25f, 4f, 0.05f);
            Slider(ls, "ResidueDropFactor", ref Settings.residueDropFactor, 0f, 4f, 0.05f);

            Section(ls, "Presentation");
            Slider(ls, "GizmoSize", ref Settings.gizmoSizeFactor, 0.4f, 1f, 0.05f);
            Toggle(ls, "CombatSounds", ref Settings.combatSounds);
            Toggle(ls, "CombatOverlays", ref Settings.combatOverlays);
            settingsHeight = ls.CurHeight + 16f;
            ls.End();
            Widgets.EndScrollView();
            if (Widgets.ButtonText(footer, "JJK_Settings_Reset".Translate()))
            {
                Settings.ResetToDefaults();
                settingsScroll = Vector2.zero;
            }
            base.DoSettingsWindowContents(inRect);
        }

        private static void Section(Listing_Standard ls, string key)
        {
            ls.GapLine();
            GameFont previous = Text.Font;
            Text.Font = GameFont.Medium;
            ls.Label(("JJK_Settings_Section" + key).Translate());
            Text.Font = previous;
        }

        private static void Toggle(Listing_Standard ls, string key, ref bool value)
            => ls.CheckboxLabeled(("JJK_Settings_" + key).Translate(), ref value,
                ("JJK_Settings_" + key + "_Tip").Translate());

        private static void Slider(Listing_Standard ls, string key, ref float value,
            float min, float max, float step, bool percent = true)
        {
            ls.Label(("JJK_Settings_" + key).Translate(percent ? value.ToString("0.#%") : value.ToString("0.###")),
                tooltip: ("JJK_Settings_" + key + "_Tip").Translate());
            float next = ls.Slider(value, min, max);
            // Preserve exact saved values unless the player moves this slider.
            if (next != value) value = Mathf.Clamp(Mathf.Round(next / step) * step, min, max);
            ls.Gap(4f);
        }
    }

    public class JJKModSettings : ModSettings
    {
        public bool randomCursedGenesOnPawnGen = true;
        public bool moodScalesCERegen = true;
        public bool enemyAIDomains = true;
        public bool corpseCurses = true;
        public float gizmoSizeFactor = 1.0f;
        public float gradeXpFactor = 1.0f;
        public bool combatSounds = true;
        public bool combatOverlays = true;
        public float naturalGeneChance = 0.07f;
        public float masteryXpFactor = 1f;
        public float rctAwakeningFactor = 1f;
        public float blackFlashChanceFactor = 1f;
        public bool curseManifestations = true;
        public bool disasterCurseIncidents = true;
        public float corpseCurseChanceFactor = 1f;
        public float corpseCurseThreshold = 8f;
        public float corpseCurseCooldownDays = 0.75f;
        public float curseThreatFactor = 1f;
        public float residueDropFactor = 1f;

        public void ResetToDefaults()
        {
            // Copy into the existing settings object: Mod owns and saves this instance.
            var defaults = new JJKModSettings();
            foreach (FieldInfo field in typeof(JJKModSettings).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                field.SetValue(this, field.GetValue(defaults));
        }

        private static float Bounded(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        public void Validate()
        {
            gizmoSizeFactor = Bounded(gizmoSizeFactor, 0.4f, 1f, 1f);
            gradeXpFactor = Bounded(gradeXpFactor, 0.25f, 4f, 1f);
            naturalGeneChance = Bounded(naturalGeneChance, 0f, 0.28f, 0.07f);
            masteryXpFactor = Bounded(masteryXpFactor, 0.25f, 4f, 1f);
            rctAwakeningFactor = Bounded(rctAwakeningFactor, 0f, 4f, 1f);
            blackFlashChanceFactor = Bounded(blackFlashChanceFactor, 0f, 4f, 1f);
            corpseCurseChanceFactor = Bounded(corpseCurseChanceFactor, 0.25f, 4f, 1f);
            corpseCurseThreshold = Bounded(corpseCurseThreshold, 1f, 40f, 8f);
            corpseCurseCooldownDays = Bounded(corpseCurseCooldownDays, 0.125f, 3f, 0.75f);
            curseThreatFactor = Bounded(curseThreatFactor, 0.25f, 4f, 1f);
            residueDropFactor = Bounded(residueDropFactor, 0f, 4f, 1f);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref randomCursedGenesOnPawnGen, "randomCursedGenesOnPawnGen", true);
            Scribe_Values.Look(ref moodScalesCERegen, "moodScalesCERegen", true);
            Scribe_Values.Look(ref enemyAIDomains, "enemyAIDomains", true);
            Scribe_Values.Look(ref corpseCurses, "corpseCurses", true);
            Scribe_Values.Look(ref gizmoSizeFactor, "gizmoSizeFactor", 1.0f);
            Scribe_Values.Look(ref gradeXpFactor, "gradeXpFactor", 1.0f);
            Scribe_Values.Look(ref combatSounds, "combatSounds", true);
            Scribe_Values.Look(ref combatOverlays, "combatOverlays", true);
            Scribe_Values.Look(ref naturalGeneChance, "naturalGeneChance", 0.07f);
            Scribe_Values.Look(ref masteryXpFactor, "masteryXpFactor", 1f);
            Scribe_Values.Look(ref rctAwakeningFactor, "rctAwakeningFactor", 1f);
            Scribe_Values.Look(ref blackFlashChanceFactor, "blackFlashChanceFactor", 1f);
            Scribe_Values.Look(ref curseManifestations, "curseManifestations", true);
            Scribe_Values.Look(ref disasterCurseIncidents, "disasterCurseIncidents", true);
            Scribe_Values.Look(ref corpseCurseChanceFactor, "corpseCurseChanceFactor", 1f);
            Scribe_Values.Look(ref corpseCurseThreshold, "corpseCurseThreshold", 8f);
            Scribe_Values.Look(ref corpseCurseCooldownDays, "corpseCurseCooldownDays", 0.75f);
            Scribe_Values.Look(ref curseThreatFactor, "curseThreatFactor", 1f);
            Scribe_Values.Look(ref residueDropFactor, "residueDropFactor", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Validate();
            base.ExposeData();
        }
    }
}
