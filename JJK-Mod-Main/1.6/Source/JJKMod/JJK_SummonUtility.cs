using RimWorld;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_SummonUtility
    {
        // Summons a shikigami. If taming, it is spawned wild and permanently
        // manhunting (hostile to everyone, including the summoner) - defeating it
        // tames it. Otherwise it is an obedient ally that fights for the summoner.
        public static Pawn SummonShadow(Pawn summoner, PawnKindDef kind, int durationTicks,
            bool taming, ShikigamiDef shikigamiDef)
        {
            if (summoner?.Map == null || kind == null) return null;

            Pawn shikigami = PawnGenerator.GeneratePawn(kind, taming ? null : summoner.Faction);

            if (!taming)
            {
                if (shikigami.Faction != summoner.Faction)
                {
                    shikigami.SetFaction(summoner.Faction, summoner);
                }
                TrainFully(summoner, shikigami);
                if (shikigami.playerSettings != null) shikigami.playerSettings.followDrafted = true;
            }

            Hediff_ShikigamiBond bond = (Hediff_ShikigamiBond)HediffMaker.MakeHediff(JJK_DefOf.JJK_ShikigamiBond, shikigami);
            bond.owner = summoner;
            bond.shikigami = shikigamiDef;
            bond.tamingInstance = taming;
            HediffComp_Disappears disappears = bond.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = durationTicks;
            shikigami.health.AddHediff(bond);

            IntVec3 cell = CellFinder.RandomClosewalkCellNear(summoner.Position, summoner.Map, taming ? 4 : 2);
            GenSpawn.Spawn(shikigami, cell, summoner.Map);
            if (!taming) ShadowInheritance.Apply(shikigami);

            if (taming && shikigami.mindState?.mentalStateHandler != null)
            {
                shikigami.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.ManhunterPermanent, forced: true, forceWake: true);
            }

            return shikigami;
        }

        private static void TrainFully(Pawn tamer, Pawn tamee)
        {
            if (tamee.training == null) return;
            foreach (TrainableDef td in DefDatabase<TrainableDef>.AllDefsListForReading)
            {
                if (tamee.training.CanAssignToTrain(td, out _).Accepted)
                {
                    tamee.training.SetWantedRecursive(td, true);
                    tamee.training.Train(td, tamer, complete: true);
                }
            }
        }
    }
}
