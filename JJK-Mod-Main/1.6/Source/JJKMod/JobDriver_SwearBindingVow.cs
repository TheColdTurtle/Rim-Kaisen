using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace JJKMod
{
    // Walk to the binding-vow altar, perform the swearing ritual, then seal the
    // chosen vow (held on the altar's comp until the ritual finishes).
    public class JobDriver_SwearBindingVow : JobDriver
    {
        private BindingVowDef chosenVow;
        public override void ExposeData() { base.ExposeData(); Scribe_Defs.Look(ref chosenVow, "chosenVow"); }
        private CompBindingVowStation Station => TargetThingA?.TryGetComp<CompBindingVowStation>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_General.Do(() => { if (chosenVow == null) chosenVow = Station?.ConsumePending(pawn); });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

            int dur = Station?.Props?.ritualDurationTicks ?? 600;
            yield return Toils_General.Wait(dur, TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

            yield return Toils_General.Do(delegate
            {
                if (chosenVow != null) BindingVowUtil.ApplyVow(pawn, chosenVow, job.targetB.Pawn);
            });
        }
    }
}
