using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class CompProperties_Shikigami : CompProperties
    {
        public CompProperties_Shikigami()
        {
            compClass = typeof(Comp_Shikigami);
        }
    }

    // Adds a "Recall to the shadows" command to tamed/allied shikigami so the
    // player can dismiss one before it dies (saving it from permanent loss).
    public class Comp_Shikigami : ThingComp
    {
        private Pawn Pawn => parent as Pawn;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn pawn = Pawn;
            if (pawn == null) yield break;

            Hediff_ShikigamiBond bond = pawn.health?.hediffSet
                ?.GetFirstHediffOfDef(JJK_DefOf.JJK_ShikigamiBond) as Hediff_ShikigamiBond;
            // Only allied (non-hostile) shikigami under player control can be recalled.
            if (bond == null || bond.tamingInstance) yield break;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) yield break;

            // Send the shikigami to attack a chosen target.
            Command_Target attack = new Command_Target
            {
                defaultLabel = "JJK_AttackTarget".Translate(),
                defaultDesc = "JJK_AttackTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false),
                targetingParams = TargetingParameters.ForAttackAny(),
                action = delegate (LocalTargetInfo target)
                {
                    if (pawn.Dead || pawn.Downed || pawn.jobs == null) return;
                    if (!(target.Thing is Thing t)) return;

                    // Use the shikigami's ranged attack if it has one, else melee.
                    Verb verb = pawn.TryGetAttackVerb(t, false);
                    Job job = (verb != null && !verb.IsMeleeAttack)
                        ? JobMaker.MakeJob(JobDefOf.AttackStatic, t)
                        : JobMaker.MakeJob(JobDefOf.AttackMelee, t);
                    job.killIncappedTarget = true;
                    job.expiryInterval = 2000;
                    job.checkOverrideOnExpire = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            };
            if (pawn.Downed) attack.Disable("JJK_ShikigamiDowned".Translate());
            yield return attack;

            // Recall the shikigami to the shadows (vanish, no loss).
            yield return new Command_Action
            {
                defaultLabel = "JJK_RecallShikigami".Translate(),
                defaultDesc = "JJK_RecallShikigamiDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", false),
                action = delegate
                {
                    // Removing the bond returns it to the shadows (vanish, no loss).
                    if (pawn.health != null && !pawn.Dead)
                    {
                        pawn.health.RemoveHediff(bond);
                    }
                }
            };
        }
    }
}
