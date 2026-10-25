using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public class CompProperties_MahoragaAdaptation : CompProperties
    {
        public int maxLevel = 3;
        public int adaptDelayTicks = 420;
        public bool fullHealOnSpin = true;
        public bool basicDeathProtection = true;
        public bool complexDeathProtection = false;
        public CompProperties_MahoragaAdaptation() { compClass = typeof(Comp_MahoragaAdaptation); }
    }

    public partial class Comp_MahoragaAdaptation : ThingComp
    {
        private Dictionary<string, int> levels = new Dictionary<string, int>();
        private Dictionary<string, int> pending = new Dictionary<string, int>();
        private Dictionary<string, int> stuns = new Dictionary<string, int>();
        private List<string> levelKeys, pendingKeys, stunKeys;
        private List<int> levelValues, pendingValues, stunValues;
        private bool regenerating;
        private CompProperties_MahoragaAdaptation Props => (CompProperties_MahoragaAdaptation)props;
        private Pawn Pawn => parent as Pawn;
        public int MaxLevel => Math.Max(1, Props.maxLevel);
        public int Spins(string key) => Math.Max(1, MahoragaPhenomena.Definition(key)?.spins > 0 ? MahoragaPhenomena.Definition(key).spins : MaxLevel);
        public int Delay(string key) => Math.Max(1, MahoragaPhenomena.Definition(key)?.delayTicks > 0 ? MahoragaPhenomena.Definition(key).delayTicks : Props.adaptDelayTicks);
        public int GetLevel(string key) => key != null && levels.TryGetValue(key, out int n) ? n : 0;
        public int PendingTicks(string key) => key != null && pending.TryGetValue(key, out int n) ? n : 0;
        public bool IsImmune(string key) => key != null && GetLevel(key) >= Spins(key);
        public float DamageFactor(string key) => Mathf.Clamp01(1f - GetLevel(key) / (float)Spins(key));
        public bool BlocksDeath(string key) => GetLevel(key) >= 1 &&
            (IsImmune(key) || (MahoragaPhenomena.Basic(key) ? Props.basicDeathProtection : Props.complexDeathProtection));
        // Compatibility helpers always mean conventional exposure, never all techniques sharing that damage.
        public int GetLevel(DamageDef def) => GetLevel(MahoragaPhenomena.Conventional(def));
        public bool IsImmune(DamageDef def) => IsImmune(MahoragaPhenomena.Conventional(def));
        public float DamageFactor(DamageDef def) => DamageFactor(MahoragaPhenomena.Conventional(def));
        public bool BlocksDeath(DamageDef def) => BlocksDeath(MahoragaPhenomena.Conventional(def));
        public void NotifyHit(DamageDef def) => NotifyHit(MahoragaPhenomena.Conventional(def));
        public void Adapt(DamageDef def) => Adapt(MahoragaPhenomena.Conventional(def));

        public void NotifyHit(string key)
        {
            if (key == null || Pawn?.Dead == true || IsImmune(key) || pending.ContainsKey(key)) return;
            pending[key] = Delay(key);
            if (Pawn?.Spawned == true)
            {
                FleckMaker.ThrowMetaIcon(Pawn.Position, Pawn.Map, FleckDefOf.IncapIcon);
                if (PawnUtility.ShouldSendNotificationAbout(Pawn))
                    Messages.Message("JJK_MahoragaWheelTurning".Translate(Pawn.LabelShortCap, MahoragaPhenomena.Label(key)), Pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }
        public void TrackStun(string key, int ticks)
        {
            if (key != null && !IsImmune(key)) stuns[key] = Find.TickManager.TicksGame + ticks;
        }
        public override void CompTick()
        {
            if (Pawn?.Dead == true) return;
            if (pending.Count > 0) foreach (string key in pending.Keys.ToList())
            {
                if (--pending[key] > 0) continue;
                pending.Remove(key);
                Adapt(key);
            }
            // A lingering control effect remains exposure even between casts.
            // Check after turns so a sustained effect can start the next spin.
            if (Pawn?.IsHashIntervalTick(30) != true) return;
            foreach (string key in stuns.Keys.ToList()) if (stuns[key] <= Find.TickManager.TicksGame) stuns.Remove(key);
            if (Pawn.stances?.stunner?.Stunned == true)
                foreach (string key in stuns.Keys.ToList()) NotifyHit(key);
            else stuns.Clear();
            foreach (Hediff h in Pawn.health.hediffSet.hediffs.ToList())
            {
                string key = MahoragaPhenomena.ControlKey(h.def);
                if (key == null) continue;
                if (IsImmune(key)) Pawn.health.RemoveHediff(h);
                else NotifyHit(key);
            }
        }
        public void Adapt(string key)
        {
            if (key == null || IsImmune(key) || Pawn?.Dead == true) return;
            levels[key] = GetLevel(key) + 1;
            if (Props.fullHealOnSpin) FullRegen();
            if (IsImmune(key))
            {
                foreach (var h in Pawn.health.hediffSet.hediffs.Where(h => MahoragaPhenomena.ControlKey(h.def) == key).ToList()) Pawn.health.RemoveHediff(h);
                if (stuns.ContainsKey(key))
                {
                    stuns.Remove(key);
                    if (stuns.Count == 0) Pawn.stances?.stunner?.StopStun();
                }
            }
            if (Pawn?.Spawned == true)
            {
                FleckMaker.Static(Pawn.Position, Pawn.Map, FleckDefOf.PsycastAreaEffect, 1.2f);
                if (PawnUtility.ShouldSendNotificationAbout(Pawn))
                    Messages.Message((IsImmune(key) ? "JJK_MahoragaImmune" : "JJK_MahoragaAdapting").Translate(Pawn.LabelShortCap, MahoragaPhenomena.Label(key)), Pawn, MessageTypeDefOf.NeutralEvent, false);
            }
        }
        public void FullRegen()
        {
            Pawn p = Pawn;
            if (p?.health == null || regenerating || p.Dead) return;
            regenerating = true;
            try
            {
                // Restore ancestors before healing surviving injuries; nested missing parts must not remain.
                foreach (var h in p.health.hediffSet.GetMissingPartsCommonAncestors().ToList()) p.health.RestorePart(h.Part);
                foreach (var h in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) p.health.RemoveHediff(h);
                var bloodLoss = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
                if (bloodLoss != null) p.health.RemoveHediff(bloodLoss);
                if (p.Spawned) FleckMaker.Static(p.Position, p.Map, FleckDefOf.PsycastAreaEffect, 2f);
            }
            finally { regenerating = false; }
        }
        public override string CompInspectStringExtra()
        {
            string[] lines = AdaptationDescription().Split('\n');
            return string.Join("\n", lines.Take(5)) + (lines.Length > 5 ? "\n" + "JJK_MahoragaMore".Translate(lines.Length - 5) : "");
        }
        public string AdaptationDescription()
        {
            var keys = levels.Keys.Concat(pending.Keys).Distinct().OrderBy(MahoragaPhenomena.Label).ToList();
            if (keys.Count == 0) return "JJK_MahoragaNoAdaptations".Translate();
            return string.Join("\n", keys.Select(key =>
                MahoragaPhenomena.Label(key) + ": " + GetLevel(key) + "/" + Spins(key)
                + (IsImmune(key) ? " — " + "JJK_MahoragaImmuneStatus".Translate() : "")
                + (pending.TryGetValue(key, out int ticks) ? " — " + "JJK_MahoragaNextSpin".Translate((ticks / 60f).ToString("0.0")) : "")
                + (!IsImmune(key) && BlocksDeath(key) ? " — " + "JJK_MahoragaDeathProtected".Translate() : "")));
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref levels, "phenomenonLevels", LookMode.Value, LookMode.Value, ref levelKeys, ref levelValues);
            Scribe_Collections.Look(ref pending, "phenomenonPending", LookMode.Value, LookMode.Value, ref pendingKeys, ref pendingValues);
            Scribe_Collections.Look(ref stuns, "phenomenonStuns", LookMode.Value, LookMode.Value, ref stunKeys, ref stunValues);
            // Old saves only grant conventional progress. Never infer technique immunity from Bomb/Cut.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Dictionary<DamageDef, int> oldLevels = null, oldPending = null;
                Scribe_Collections.Look(ref oldLevels, "levels", LookMode.Def, LookMode.Value);
                Scribe_Collections.Look(ref oldPending, "pending", LookMode.Def, LookMode.Value);
                levels = levels ?? new Dictionary<string, int>(); pending = pending ?? new Dictionary<string, int>();
                if (oldLevels != null) foreach (var pair in oldLevels) { string key = MahoragaPhenomena.Conventional(pair.Key); if (key != null) levels[key] = Math.Max(GetLevel(key), pair.Value); }
                if (oldPending != null) foreach (var pair in oldPending) { string key = MahoragaPhenomena.Conventional(pair.Key); if (key != null && !pending.ContainsKey(key)) pending[key] = pair.Value; }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                levels = levels ?? new Dictionary<string, int>(); pending = pending ?? new Dictionary<string, int>(); stuns = stuns ?? new Dictionary<string, int>();
                foreach (var key in levels.Keys.ToList()) levels[key] = Mathf.Clamp(levels[key], 0, Spins(key));
                foreach (var key in pending.Keys.ToList()) { if (IsImmune(key)) pending.Remove(key); else pending[key] = Mathf.Clamp(pending[key], 1, Delay(key)); }
            }
        }
    }
}
