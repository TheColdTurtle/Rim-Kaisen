using System.Collections.Generic;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Marks a Cursed Speech ability. Its effect scales with the caster's Talking
    // stat, and speaking the word cuts at the caster's tongue by voiceStrain
    // (heavier words hurt more). A wrecked tongue lowers Talking, which in turn
    // weakens future cursed speech - so overusing the heavy words spirals.
    public class CursedSpeechExtension : DefModExtension
    {
        public float voiceStrain = 1f;
    }

    public static class CursedSpeechUtil
    {
        private static BodyPartDef voicePartCached;
        private static bool voiceResolved;

        // The tongue: a fleshy part (so a Cut actually bleeds, unlike the solid jaw
        // bone which only ever "cracks"), and the part that governs the Talking
        // capacity - so straining it both bleeds and weakens future cursed speech.
        private static BodyPartDef VoicePart
        {
            get
            {
                if (!voiceResolved)
                {
                    voicePartCached = DefDatabase<BodyPartDef>.GetNamedSilentFail("Tongue");
                    voiceResolved = true;
                }
                return voicePartCached;
            }
        }

        // Multiplier applied to cursed speech output. A mute pawn (Talking ~0)
        // barely affects anything; a silver-tongued sorcerer hits far harder.
        public static float TalkingFactor(Pawn p)
        {
            return Mathf.Clamp(p?.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 0f, 0f, 2f);
        }

        // Speaking a cursed word cuts at the tongue, leaving a small bleeding wound.
        // Damage is dealt to the tongue if the pawn has one (so it bleeds and can
        // eventually be wrecked, muting the speaker), otherwise no strain injury is applied.
        public static void StrainVoice(Pawn pawn, float amount)
        {
            if (pawn == null || pawn.Dead || amount <= 0f) return;

            BodyPartRecord tongue = null;
            BodyPartDef voiceDef = VoicePart;
            if (voiceDef != null)
            {
                foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
                {
                    if (part.def == voiceDef) { tongue = part; break; }
                }
            }

            // A missing tongue must not redirect vocal strain into a random limb.
            if (tongue == null) return;
            // DamageWorker rounds sub-point damage, intermittently dropping a 0.5
            // strain altogether. Add an exact Cut injury for deterministic slight
            // bleeding. This internal strain also cannot be intercepted by Infinity.
            float health = pawn.health.hediffSet.GetPartHealth(tongue);
            Hediff cut = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("JJK_CursedSpeechCut"), pawn, tongue);
            cut.Severity = Mathf.Min(amount, health);
            pawn.health.AddHediff(cut, tongue);
            if (amount >= health)
                pawn.health.AddHediff(HediffDefOf.MissingBodyPart, tongue);
        }
    }

    // Shared base for cursed speech moves. The spoken word lands instantly - it is
    // a command the body must obey, not a projectile - so a moving target can never
    // dodge it. Each word's strength scales with the caster's Talking capacity.
    public abstract class Ability_CursedSpeech : Ability
    {
        protected float Factor => CursedSpeechUtil.TalkingFactor(pawn);

        public override System.Func<string> CanBeActivatedValidator() => () =>
            Factor <= 0.01f ? "JJK_SpeechCannotSpeak".Translate().ToString() : "";

        // Deal a damage total split into small chunks across the body, so a heavy
        // word actually maims rather than wasting itself on one body part.
        protected void DealChunks(Pawn target, float total, DamageDef damageDef, float armorPen)
        {
            if (target == null || target.Dead) return;
            const float chunk = 12f;
            for (float remaining = total; remaining > 0f; remaining -= chunk)
            {
                if (target.Dead) break;
                target.TakeDamage(new DamageInfo(damageDef, Mathf.Min(chunk, remaining), armorPen, -1f, pawn));
            }
        }
    }

    // "Don't move": instantly stun the target in place. Duration scales with Talking.
    public class Ability_SpeechDontMove : Ability_CursedSpeech
    {
        private const int BaseStunTicks = 150;

        public override void Start(bool consumeEnergy = true)
        {
            float factor = Factor;
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead || target.stances?.stunner == null) return;
            target.stances.stunner.StunFor(Mathf.RoundToInt(BaseStunTicks * factor), pawn, true, true);
        }
    }

    // "Blast away": instantly batter and briefly stun every hostile around the
    // target point. The shout cannot miss.
    public class Ability_SpeechBlastAway : Ability_CursedSpeech
    {
        private const float Radius = 3.4f;
        private const float BaseDamage = 26f;
        private const int StaggerTicks = 45;

        public override void Start(bool consumeEnergy = true)
        {
            float f = Factor;
            base.Start(consumeEnergy);
            Map map = pawn?.Map;
            if (map == null) return;
            IntVec3 center = curTarget.Cell;

            foreach (Thing t in GenRadial.RadialDistinctThingsAround(center, map, Radius, true))
            {
                if (!(t is Pawn p) || p.Dead || p == pawn) continue;
                if (!p.HostileTo(pawn)) continue;
                DealChunks(p, BaseDamage * f, DamageDefOf.Blunt, 0f);
                if (!p.Dead && p.stances?.stunner != null)
                    p.stances.stunner.StunFor(StaggerTicks, pawn, false, true);
            }
        }
    }

    // "Crush": instantly inflict heavy, armor-cleaving damage on a single target.
    public class Ability_SpeechCrush : Ability_CursedSpeech
    {
        private const float BaseDamage = 65f;

        public override void Start(bool consumeEnergy = true)
        {
            float factor = Factor;
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            if (target == null || target.Dead) return;
            DealChunks(target, BaseDamage * factor, DamageDefOf.Blunt, 0.8f);
        }
    }
}
