using Verse;

namespace JJKMod
{
    // Per-sorcerer state of a single shikigami.
    public enum ShikigamiState : byte
    {
        Untamed = 0, // never tamed; summoning produces a hostile instance
        Available = 1, // tamed (or a dog); summoning produces an ally
        Lost = 2      // a tamed instance was killed; can never be summoned again
    }

    // Defines one Ten Shadows shikigami and how it is summoned/tamed.
    public class ShikigamiDef : Def
    {
        // The pawnkind summoned as an ally (and, while untamed, as the hostile
        // instance the sorcerer must defeat to tame it).
        public PawnKindDef pawnKind;

        // Dogs only: the other dog. If the sibling has been permanently lost,
        // summoning this one produces the stronger totalityPawnKind instead.
        public ShikigamiDef siblingDog;
        public PawnKindDef totalityPawnKind;

        // Dogs do not need taming and start Available; everything else starts
        // Untamed and must be defeated once before it can be used.
        public bool needsTaming = true;

        // Allied summon lifetime (returns to the shadows on expiry, no loss).
        public int summonDurationTicks = 60000;   // ~1 day
        // Hostile taming-instance lifetime (vanishes untamed if not killed).
        public int tamingDurationTicks = 120000;  // ~2 days

        // How many are summoned per cast (Rabbit Escape summons a swarm).
        public int summonCount = 1;

        // If false, a tamed instance dying does NOT lose the shadow (expendable
        // swarm members like rabbits). The taming instance still tames on death.
        public bool losableOnDeath = true;

        public ShikigamiState DefaultState => needsTaming ? ShikigamiState.Untamed : ShikigamiState.Available;
    }

    // Attached to a summon AbilityDef to point it at its ShikigamiDef.
    public class ShikigamiAbilityExtension : DefModExtension
    {
        public ShikigamiDef shikigami;
    }
}
