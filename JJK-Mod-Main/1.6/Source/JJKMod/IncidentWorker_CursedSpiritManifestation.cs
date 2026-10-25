using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Cursed spirits manifest near the colony and attack. A points-scaled pack of
    // curses (any pawnkind whose race carries a CursedSpiritExtension) enters from
    // a map edge as permanent manhunters - drawn to the world's suffering. A
    // storyteller threat; killing them exorcises them (HarmonyPatches drops residue).
    public class IncidentWorker_CursedSpiritManifestation : IncidentWorker
    {
        private const int MaxCurses = 40;

        public override bool CanFireNowSub(IncidentParms parms)
        {
            return (RimKaisenMod.Settings?.curseManifestations ?? true)
                && base.CanFireNowSub(parms)
                && parms.target is Map
                && CursedSpiritUtil.CurseKinds().Count > 0;
        }

        public override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(RimKaisenMod.Settings?.curseManifestations ?? true) || !(parms.target is Map map)) return false;

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map,
                    CellFinder.EdgeRoadChance_Animal))
            {
                return false;
            }

            float points = Mathf.Max(parms.points, 200f) * (RimKaisenMod.Settings?.curseThreatFactor ?? 1f);
            List<Pawn> spawned = CursedSpiritUtil.SpawnPack(map, entry, points, MaxCurses);
            if (spawned.Count == 0) return false;

            SendStandardLetter(parms, new LookTargets(spawned), spawned.Count.ToString());
            return true;
        }
    }
}
