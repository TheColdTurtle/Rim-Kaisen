using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Curses are born of death and lingering negative emotion. The more corpses
    // left lying on a home map, the more likely cursed spirits are to coalesce out
    // of them and attack - so disposing of bodies (graves, cremation) is a real
    // safeguard. Buried corpses (inside graves) are not counted; only those still
    // out in the open or stockpiled. Humanlike dead weigh far more than animals.
    public class MapComponent_CursedSpiritSpawner : MapComponent
    {
        private int nextCheckTick;
        private int cooldownUntilTick;

        private const int CheckIntervalTicks = 2500;  // ~1 in-game hour
        private const float ChancePerScore = 0.012f;   // per point of score over threshold
        private const float MaxChance = 0.4f;

        public MapComponent_CursedSpiritSpawner(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (RimKaisenMod.Settings != null && !RimKaisenMod.Settings.corpseCurses) return;

            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick) return;
            nextCheckTick = now + CheckIntervalTicks;
            if (now < cooldownUntilTick || !map.IsPlayerHome) return;

            float score = CorpseScore();
            float threshold = RimKaisenMod.Settings?.corpseCurseThreshold ?? 8f;
            if (score <= threshold) return;

            float chance = Mathf.Clamp01(Mathf.Min(MaxChance, (score - threshold) * ChancePerScore)
                * (RimKaisenMod.Settings?.corpseCurseChanceFactor ?? 1f));
            if (!Rand.Chance(chance)) return;

            if (ManifestFromCorpses(score))
                cooldownUntilTick = now + Mathf.RoundToInt(60000f * (RimKaisenMod.Settings?.corpseCurseCooldownDays ?? 0.75f));
        }

        // Weighted count of corpses lying on the map. Humanlike dead drive curses;
        // animal carcasses contribute only a little.
        private float CorpseScore()
        {
            float score = 0f;
            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                if (!(corpses[i] is Corpse c) || !c.Spawned) continue;
                bool humanlike = c.InnerPawn?.RaceProps?.Humanlike ?? false;
                score += humanlike ? 1f : 0.15f;
            }
            return score;
        }

        private bool ManifestFromCorpses(float score)
        {
            // Rise from the first spawned corpse in the map's corpse list.
            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            Corpse origin = null;
            for (int i = 0; i < corpses.Count; i++)
            {
                if (corpses[i] is Corpse c && c.Spawned) { origin = c; break; }
            }
            if (origin == null) return false;

            float points = Mathf.Clamp(score * 20f, 120f, 900f) * (RimKaisenMod.Settings?.curseThreatFactor ?? 1f);
            List<Pawn> spawned = CursedSpiritUtil.SpawnPack(map, origin.Position, points);
            if (spawned.Count == 0) return false;

            FleckMaker.ThrowSmoke(origin.DrawPos, map, 3f);
            Find.LetterStack.ReceiveLetter(
                "JJK_CorpseCurseLabel".Translate(),
                "JJK_CorpseCurseText".Translate(spawned.Count),
                LetterDefOf.ThreatBig,
                new LookTargets(spawned));
            return true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextCheckTick, "jjk_corpseNextCheckTick", 0);
            Scribe_Values.Look(ref cooldownUntilTick, "jjk_corpseCooldownUntilTick", 0);
        }
    }
}
