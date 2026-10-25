using UnityEngine;
using RimWorld;
using Verse;

namespace JJKMod
{
    // The wall of a closed Domain Expansion, implemented as a door so the engine
    // can make it passable for some pawns and not others natively. Only a
    // heavenly-restriction sorcerer with no cursed energy (Maki / Toji) can open
    // it; for everyone else it behaves as a sealed, impassable, sight-blocking
    // wall, and the pathfinder routes them around it (or walls them in).
    public class DomainBarrierDoor : Building_Door
    {
        public override bool PawnCanOpen(Pawn p)
        {
            return JJK_Utils.CanPhaseDomainBarrier(p);
        }

        // Building_Door draws two sliding door halves; with the placeholder texture
        // that reads as nothing. Draw a solid, tinted full-cell quad instead so the
        // barrier is clearly visible (the door open/close logic is untouched).
        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Material mat = Graphic?.MatSingleFor(this);
            if (mat == null) return;
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.identity, Vector3.one);
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
    }
}
