using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // A Disaster Curse walks the earth: one named boss curse enters the map with
    // an escort of lesser curses. Rare, late-game. Exorcising the boss drops its
    // technique remnant (CursedSpiritExtension.bossDrop) - the only way to learn
    // a Disaster Curse's innate technique.
    public class IncidentWorker_DisasterCurse : IncidentWorker
    {
        private static readonly List<string> BossKindNames = new List<string>
        {
            "JJK_Boss_Jogo_Kind",
            "JJK_Boss_Hanami_Kind",
            "JJK_Boss_CursedWomb_Kind",   // evolves into Dagon
            "JJK_Boss_Mahito_Kind",
        };

        public override bool CanFireNowSub(IncidentParms parms)
        {
            return (RimKaisenMod.Settings?.disasterCurseIncidents ?? true)
                && base.CanFireNowSub(parms) && parms.target is Map && BossKinds().Count > 0;
        }

        public override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(RimKaisenMod.Settings?.disasterCurseIncidents ?? true) || !(parms.target is Map map)) return false;

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map,
                    CellFinder.EdgeRoadChance_Animal))
            {
                return false;
            }

            List<PawnKindDef> kinds = BossKinds();
            if (kinds.Count == 0) return false;
            PawnKindDef bossKind = kinds.RandomElement();

            Pawn boss = PawnGenerator.GeneratePawn(bossKind, null);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(entry, map, 4);
            GenSpawn.Spawn(boss, cell, map, Rot4.Random);

            // Escort of lesser curses scaled off the remaining threat points.
            float escortPoints = Mathf.Max(150f, parms.points * 0.5f) * (RimKaisenMod.Settings?.curseThreatFactor ?? 1f);
            List<Pawn> escort = CursedSpiritUtil.SpawnPack(map, entry, escortPoints, 20);

            var targets = new List<Pawn> { boss };
            targets.AddRange(escort);
            SendStandardLetter(parms, new LookTargets(targets), boss.kindDef.LabelCap);
            return true;
        }

        private static List<PawnKindDef> BossKinds()
        {
            var list = new List<PawnKindDef>();
            for (int i = 0; i < BossKindNames.Count; i++)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(BossKindNames[i]);
                if (kind != null) list.Add(kind);
            }
            return list;
        }
    }
}
