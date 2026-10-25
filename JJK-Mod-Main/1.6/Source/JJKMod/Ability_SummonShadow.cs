using System;
using RimWorld;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Generic Ten Shadows summon. The shadow it summons is read from the
    // AbilityDef's ShikigamiAbilityExtension. Behaviour depends on the
    // sorcerer's stored state for that shadow:
    //   Untamed (non-dogs) -> spawn a hostile instance to defeat (taming).
    //   Available (dogs, or tamed) -> spawn an obedient ally. If a sibling dog
    //                                 has been lost, the surviving dog is summoned
    //                                 in its stronger Totality form.
    //   Lost -> cannot be summoned (blocked in the validator).
    public class Ability_SummonShadow : Ability
    {
        public Pawn activeInstance;

        public ShikigamiDef Shadow => def.GetModExtension<ShikigamiAbilityExtension>()?.shikigami;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);

            ShikigamiDef shadow = Shadow;
            if (shadow == null) return;
            TenShadowsData data = GameComponent_JJK.Instance?.GetTenShadows(pawn);
            if (data == null) return;

            ShikigamiState state = data.GetState(shadow);
            bool taming = shadow.needsTaming && state == ShikigamiState.Untamed;

            PawnKindDef kind = shadow.pawnKind;
            int duration = taming ? shadow.tamingDurationTicks : shadow.summonDurationTicks;

            // Dog -> Totality when the sibling dog is permanently lost.
            if (!taming && shadow.siblingDog != null && shadow.totalityPawnKind != null
                && data.GetState(shadow.siblingDog) == ShikigamiState.Lost)
            {
                kind = shadow.totalityPawnKind;
            }

            // Taming always manifests a single instance; allies summon the full
            // group (a swarm, for Rabbit Escape).
            int count = taming ? 1 : Math.Max(1, shadow.summonCount);
            for (int i = 0; i < count; i++)
            {
                Pawn spawned = JJK_SummonUtility.SummonShadow(pawn, kind, duration, taming, shadow);
                if (spawned != null) activeInstance = spawned;
            }

            if (taming)
            {
                Messages.Message("JJK_ShikigamiTamingStarted".Translate(shadow.LabelCap),
                    activeInstance, MessageTypeDefOf.CautionInput, false);
            }
        }

        public override Func<string> CanBeActivatedValidator()
        {
            return delegate
            {
                ShikigamiDef shadow = Shadow;
                if (shadow == null) return "";

                if (!activeInstance.DestroyedOrNull() && !activeInstance.Dead)
                {
                    return "JJK_ShikigamiAlreadySummoned".Translate();
                }

                TenShadowsData data = GameComponent_JJK.Instance?.GetTenShadows(pawn);
                if (data != null && data.GetState(shadow) == ShikigamiState.Lost)
                {
                    return "JJK_ShikigamiLost".Translate(shadow.LabelCap);
                }
                return "";
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref activeInstance, "activeInstance");
        }
    }
}
