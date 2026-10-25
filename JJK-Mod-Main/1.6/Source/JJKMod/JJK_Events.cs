using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // ===================================================================== //
    // Two canon jujutsu events.
    // ===================================================================== //

    // THE VEIL (帳). Sorcerers drop a curtain over an area so the ordinary world
    // does not see what happens inside it - and cursed spirits, drawn to a place
    // sealed off from help, crowd in. Mechanically: the sky goes dark and curses
    // keep manifesting for as long as the veil stands. It is a siege condition,
    // not a single fight: the pressure is the point.
    public class GameCondition_Veil : GameCondition
    {
        // One manifestation per in-game hour. Deliberately modest per wave - the
        // threat is the attrition of a condition that lasts hours, not one alpha
        // strike, and the storyteller has no idea this is happening so it must not
        // be able to wipe a colony on its own.
        private const int SpawnIntervalTicks = 2500;

        // Points per wave, scaled off colony wealth by the incident, stored here so
        // every wave is the same size the letter warned about.
        public float wavePoints = 200f;

        private static readonly SkyColorSet VeilColors = new SkyColorSet(
            new Color(0.34f, 0.28f, 0.44f),   // sky: bruised violet
            new Color(0.55f, 0.50f, 0.62f),   // shadows
            new Color(0.38f, 0.32f, 0.48f),   // overlay
            0.85f);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wavePoints, "jjkVeilWavePoints", 200f);
        }

        public override void GameConditionTick()
        {
            base.GameConditionTick();
            if (Find.TickManager.TicksGame % SpawnIntervalTicks != 0) return;

            foreach (Map map in AffectedMaps)
            {
                if (map == null) continue;
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map,
                        CellFinder.EdgeRoadChance_Animal))
                {
                    continue;
                }
                CursedSpiritUtil.SpawnPack(map, entry, wavePoints, 12);
            }
        }

        // A veil blots out the sky inside it - this is what sells the condition
        // visually, and it also tells the player at a glance that it is still up.
        public override SkyTarget? SkyTarget(Map map) =>
            new SkyTarget(0.55f, VeilColors, 1f, 0f);

        public override float SkyGazeChanceFactor(Map map) => 0f;
        public override float SkyGazeJoyGainFactor(Map map) => 0f;
    }

    // A SUKUNA'S FINGER SURFACES. The King of Curses' fingers are the single most
    // cursed objects in the world, and curses swarm to them. Finding one is the
    // main way a colony gets its hands on a finger without killing a Finger Bearer
    // - but it never comes alone, and leaving it on the ground does not make the
    // curses drawn to it go home.
    public class IncidentWorker_SukunaFingerUnearthed : IncidentWorker
    {
        public override bool CanFireNowSub(IncidentParms parms)
        {
            return base.CanFireNowSub(parms)
                   && parms.target is Map
                   && JJK_DefOf.JJK_SukunaFinger != null;
        }

        public override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map)) return false;
            if (JJK_DefOf.JJK_SukunaFinger == null) return false;

            // Put the finger somewhere the colony can actually reach and see, not
            // sealed in rock - it is meant to be a decision, not a scavenger hunt.
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(
                    map.Center, map, out IntVec3 cell))
            {
                if (!CellFinderLoose.TryGetRandomCellWith(
                        c => c.Standable(map) && !c.Fogged(map), map, 1000, out cell))
                {
                    return false;
                }
            }

            Thing finger = ThingMaker.MakeThing(JJK_DefOf.JJK_SukunaFinger);
            GenPlace.TryPlaceThing(finger, cell, map, ThingPlaceMode.Near);

            // Curses converge on it. Scaled off the incident's own threat points so
            // it stays proportional to the colony rather than being a flat tax.
            float points = Mathf.Max(120f, parms.points * 0.35f);
            List<Pawn> drawn = CursedSpiritUtil.SpawnPack(map, cell, points, 10);

            var look = new List<Thing> { finger };
            foreach (Pawn p in drawn) look.Add(p);

            SendStandardLetter(parms, new LookTargets(look));
            return true;
        }
    }
}
