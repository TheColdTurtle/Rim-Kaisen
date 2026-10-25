using System.Collections.Generic;
using Verse;

namespace JJKMod
{
    // Per-sorcerer Ten Shadows roster state. Stored in GameComponent_JJK and
    // scribed with the game.
    public class TenShadowsData : IExposable
    {
        private Dictionary<ShikigamiDef, ShikigamiState> states = new Dictionary<ShikigamiDef, ShikigamiState>();

        private List<ShikigamiDef> keysWork;
        private List<ShikigamiState> valsWork;

        public ShikigamiState GetState(ShikigamiDef def)
        {
            if (def == null) return ShikigamiState.Untamed;
            return states.TryGetValue(def, out ShikigamiState s) ? s : def.DefaultState;
        }

        public void SetState(ShikigamiDef def, ShikigamiState state)
        {
            if (def == null) return;
            states[def] = state;
        }

        public bool CanSummon(ShikigamiDef def)
        {
            return def != null && GetState(def) != ShikigamiState.Lost;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref states, "states", LookMode.Def, LookMode.Value,
                ref keysWork, ref valsWork);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && states == null)
            {
                states = new Dictionary<ShikigamiDef, ShikigamiState>();
            }
        }
    }
}
