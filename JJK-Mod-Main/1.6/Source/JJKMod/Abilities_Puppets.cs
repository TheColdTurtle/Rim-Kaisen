using System;
using System.Linq;
using RimWorld;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    public class PuppetAbilityExtension : DefModExtension
    {
        public PuppetMove move;
        public bool recharge;
    }
    public class Ability_PuppetOrder : Ability
    {
        private PuppetAbilityExtension Props => def.GetModExtension<PuppetAbilityExtension>();
        private bool Support => Props.recharge || Props.move == PuppetMove.UltraShield || Props.move == PuppetMove.BoostOn;
        public CompCursedCorpse Source(Pawn target)
        {
            if (pawn?.Spawned != true || !PuppetUtility.Manipulator(pawn)) return null;
            if (Support)
            {
                var c = target?.TryGetComp<CompCursedCorpse>();
                return c?.controller == pawn && target.Spawned && target.Map == pawn.Map && (Props.recharge || c.Mech) ? c : null;
            }
            return GameComponent_Puppets.Instance.units.Where(p => p != null && p.Spawned && p.Map == pawn.Map)
                .Select(p => p.TryGetComp<CompCursedCorpse>()).Where(c => c?.controller == pawn && c.MoveReason(Props.move, target) == null)
                .OrderBy(c => c.Unit.Position.DistanceToSquared(target.Position)).FirstOrDefault();
        }
        public override void Start(bool consumeEnergy = true)
        {
            var source = Source(curTarget.Pawn);
            if (source == null) { Messages.Message("JJK_PuppetNoSource".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            bool done = Props.recharge ? source.Recharge(pawn) : source.TryMove(Props.move, curTarget.Pawn);
            if (!done) { Messages.Message("JJK_PuppetNoSource".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            // Energy is transferred to the core explicitly; the TMF tier costs zero.
            base.Start(consumeEnergy);
        }
        public override Func<string> CanBeActivatedValidator() => () =>
            GameComponent_Puppets.Instance.units.Any(p => p?.Spawned == true && p.Map == pawn.Map
                && p.TryGetComp<CompCursedCorpse>()?.controller == pawn) ? null : "JJK_PuppetNoSource".Translate().ToString();
    }
}
