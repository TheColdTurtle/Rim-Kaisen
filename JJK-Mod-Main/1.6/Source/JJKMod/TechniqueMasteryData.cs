using System.Collections.Generic;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // Per-sorcerer record of how deeply they have mastered each cursed technique,
    // measured as accumulated XP per technique tree. Earned by casting the
    // technique's abilities and by landing kills while using it (HarmonyPatches).
    // Used to decide Domain Expansion clashes: when two domains overlap, the
    // caster with the greater effective mastery in their domain's technique wins.
    public class TechniqueMasteryData : IExposable
    {
        private Dictionary<AbilityTreeDef, int> xp = new Dictionary<AbilityTreeDef, int>();

        private List<AbilityTreeDef> keysWorking;
        private List<int> valuesWorking;

        public int Get(AbilityTreeDef tree)
        {
            if (tree == null) return 0;
            return xp.TryGetValue(tree, out int v) ? v : 0;
        }

        public void Add(AbilityTreeDef tree, int amount)
        {
            if (tree == null || amount <= 0) return;
            xp.TryGetValue(tree, out int current);
            xp[tree] = current + amount;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref xp, "xp", LookMode.Def, LookMode.Value,
                ref keysWorking, ref valuesWorking);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (xp == null) xp = new Dictionary<AbilityTreeDef, int>();
                xp.RemoveAll(kvp => kvp.Key == null);
            }
        }
    }
}
