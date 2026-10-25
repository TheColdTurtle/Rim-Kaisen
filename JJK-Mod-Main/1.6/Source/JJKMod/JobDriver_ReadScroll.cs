using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace JJKMod
{
    // Walk to a technique scroll, study it, then apply its learn effect.
    public class JobDriver_ReadScroll : JobDriver
    {
        private CompLearnAbilityScroll Scroll => TargetThingA?.TryGetComp<CompLearnAbilityScroll>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

            int dur = Scroll?.Props?.readDurationTicks ?? 300;
            yield return Toils_General.Wait(dur, TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

            yield return Toils_General.Do(delegate
            {
                Scroll?.DoEffect(pawn);
            });
        }
    }
}
