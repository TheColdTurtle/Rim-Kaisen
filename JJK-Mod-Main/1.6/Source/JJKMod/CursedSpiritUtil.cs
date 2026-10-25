using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public static class CursedSpiritUtil
    {
        // Every generic curse pawnkind (any whose race carries a
        // CursedSpiritExtension). Unique named bosses are excluded - they only
        // arrive through their own incident.
        public static List<PawnKindDef> CurseKinds()
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(k =>
                {
                    var ext = k.race?.GetModExtension<CursedSpiritExtension>();
                    return ext != null && !ext.uniqueBoss;
                })
                .ToList();
        }

        // Spawn a points-scaled pack of curses clustered around a centre cell.
        // They spawn factionless, so Patch_CurseManhunter makes them permanent
        // manhunters automatically. Returns the curses spawned.
        public static List<Pawn> SpawnPack(Map map, IntVec3 center, float points, int maxCount = 40)
        {
            List<Pawn> spawned = new List<Pawn>();
            if (map == null) return spawned;
            List<PawnKindDef> kinds = CurseKinds();
            if (kinds.Count == 0) return spawned;

            float remaining = Mathf.Max(points, 50f);
            int safety = 0;
            while (remaining > 0f && spawned.Count < maxCount && safety++ < 300)
            {
                List<PawnKindDef> affordable = kinds.Where(k => k.combatPower <= remaining + 40f).ToList();
                if (affordable.Count == 0) break;

                PawnKindDef kind = affordable.RandomElement();
                Pawn curse = PawnGenerator.GeneratePawn(kind, null);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 6);
                GenSpawn.Spawn(curse, cell, map, Rot4.Random);
                spawned.Add(curse);
                remaining -= kind.combatPower;
            }
            return spawned;
        }
    }
}
