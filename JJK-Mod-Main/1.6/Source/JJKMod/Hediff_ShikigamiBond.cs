using RimWorld;
using Verse;

namespace JJKMod
{
    // Rides on every summoned shikigami. Carries the link back to the summoner
    // and which shadow this is, plus whether it is the hostile taming instance.
    // The HediffComp_Disappears timer drives the "returns to the shadows" expiry;
    // death (vs. expiry) is handled in the Pawn.Kill patch.
    public class Hediff_ShikigamiBond : HediffWithComps
    {
        public Pawn owner;
        public ShikigamiDef shikigami;
        public bool tamingInstance;

        public override void PostRemoved()
        {
            base.PostRemoved();
            // Reached only on timed expiry (a killed shikigami is already Dead and
            // handled in Pawn.Kill). The shikigami simply returns to the shadows.
            if (pawn != null && pawn.Spawned && !pawn.Dead)
            {
                if (pawn.Map != null) FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 1.5f);
                pawn.Destroy(DestroyMode.Vanish);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Defs.Look(ref shikigami, "shikigami");
            Scribe_Values.Look(ref tamingInstance, "tamingInstance", false);
        }
    }
}
