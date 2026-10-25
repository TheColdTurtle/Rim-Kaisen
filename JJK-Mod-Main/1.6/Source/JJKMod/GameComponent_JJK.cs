using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    // Persistent per-game store of sorcerer grade XP and Calamity unlocks,
    // and the ticker that keeps each sorcerer's grade hediff in sync.
    public class GameComponent_JJK : GameComponent
    {
        private Dictionary<Pawn, int> gradeXp = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, CastPracticeAllowance> castPractice = new Dictionary<Pawn, CastPracticeAllowance>();
        private List<Pawn> castKeysWorking;
        private List<CastPracticeAllowance> castValuesWorking;

        public int CastPracticeRemaining(Pawn pawn)
        {
            if (pawn == null || !castPractice.TryGetValue(pawn, out var allowance)
                || Find.TickManager.TicksGame - allowance.windowStart >= 60000) return CastPracticeAllowance.Limit;
            return System.Math.Max(0, CastPracticeAllowance.Limit - allowance.used);
        }

        public int CastPracticeResetTicks(Pawn pawn)
        {
            if (pawn == null || !castPractice.TryGetValue(pawn, out var allowance)) return 0;
            return System.Math.Max(0, allowance.windowStart + 60000 - Find.TickManager.TicksGame);
        }

        public bool TryRewardCastPractice(Pawn pawn, float energyCost)
        {
            if (pawn == null || energyCost <= 0f) return false;
            if (!castPractice.TryGetValue(pawn, out var allowance))
                castPractice[pawn] = allowance = new CastPracticeAllowance();
            return allowance.TryConsume(Find.TickManager.TicksGame);
        }
        private HashSet<Pawn> calamityUnlocked = new HashSet<Pawn>();
        private Dictionary<Pawn, TenShadowsData> tenShadows = new Dictionary<Pawn, TenShadowsData>();
        private Dictionary<Pawn, TechniqueMasteryData> techniqueMastery = new Dictionary<Pawn, TechniqueMasteryData>();
        // Idle Transfiguration: how many transfigured souls each sorcerer has stored.
        private Dictionary<Pawn, int> storedSouls = new Dictionary<Pawn, int>();
        // Cursed Spirit Manipulation: the curses each manipulator has absorbed.
        private Dictionary<Pawn, AbsorbedCurseData> absorbedCurses = new Dictionary<Pawn, AbsorbedCurseData>();
        // How much insight into OTHER bodies each RCT sorcerer has accumulated by
        // tending the wounded. At the threshold they awaken as a soul mender.
        private Dictionary<Pawn, int> menderInsight = new Dictionary<Pawn, int>();

        // The technique tree of each sorcerer's most recent cursed-technique cast,
        // so a kill can be credited to the technique they were actively using.
        // Transient (not worth persisting across a save) - rebuilds as they cast.
        private readonly Dictionary<Pawn, AbilityTreeDef> lastCastTree = new Dictionary<Pawn, AbilityTreeDef>();

        // How much effective technique mastery each sorcerer grade is worth on its
        // own, so a fresh higher-grade sorcerer still out-clashes a fresh lower one,
        // while accumulated XP lets a lower-grade veteran overtake a higher novice.
        private const int GradeMasteryBaseline = 400;

        // Scribe working lists.
        private List<Pawn> xpKeysWorking;
        private List<int> xpValuesWorking;
        private List<Pawn> shadowKeysWorking;
        private List<TenShadowsData> shadowValuesWorking;
        private List<Pawn> masteryKeysWorking;
        private List<TechniqueMasteryData> masteryValuesWorking;
        private List<Pawn> soulKeysWorking;
        private List<int> soulValuesWorking;
        private List<Pawn> curseKeysWorking;
        private List<AbsorbedCurseData> curseValuesWorking;
        private List<Pawn> menderKeysWorking;
        private List<int> menderValuesWorking;

        public GameComponent_JJK(Game game) { }

        public static GameComponent_JJK Instance => Current.Game?.GetComponent<GameComponent_JJK>();

        // Per-sorcerer Ten Shadows roster (created on first access).
        public TenShadowsData GetTenShadows(Pawn pawn)
        {
            if (pawn == null) return null;
            if (!tenShadows.TryGetValue(pawn, out TenShadowsData data))
            {
                data = new TenShadowsData();
                tenShadows[pawn] = data;
            }
            return data;
        }

        // Per-sorcerer technique mastery store (created on first access).
        public TechniqueMasteryData GetMastery(Pawn pawn)
        {
            if (pawn == null) return null;
            if (!techniqueMastery.TryGetValue(pawn, out TechniqueMasteryData data))
            {
                data = new TechniqueMasteryData();
                techniqueMastery[pawn] = data;
            }
            return data;
        }

        // Raw accumulated mastery XP in a technique.
        public int GetTechniqueXp(Pawn pawn, AbilityTreeDef tree)
        {
            if (pawn == null || tree == null) return 0;
            return techniqueMastery.TryGetValue(pawn, out TechniqueMasteryData d) ? d.Get(tree) : 0;
        }

        // Effective mastery used to decide a domain clash: accumulated XP plus a
        // baseline from the sorcerer's grade. Lets grade decide between novices,
        // while a dedicated lower-grade master can overtake a higher-grade novice.
        public int GetEffectiveMastery(Pawn pawn, AbilityTreeDef tree)
        {
            if (pawn == null) return 0;
            return GetTechniqueXp(pawn, tree) + (int)GetGrade(pawn) * GradeMasteryBaseline;
        }

        public void AddTechniqueXp(Pawn pawn, AbilityTreeDef tree, int amount)
        {
            if (pawn == null || tree == null || amount <= 0) return;
            if (!JJK_GradeUtility.IsSorcerer(pawn)) return;
            float factor = RimKaisenMod.Settings?.masteryXpFactor ?? 1f;
            GetMastery(pawn).Add(tree, UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(amount * factor)));
        }

        // Idle Transfiguration stored-soul bank.
        public int GetStoredSouls(Pawn pawn)
        {
            if (pawn == null) return 0;
            return storedSouls.TryGetValue(pawn, out int n) ? n : 0;
        }

        public void AddStoredSoul(Pawn pawn, int count = 1)
        {
            if (pawn == null || count <= 0) return;
            storedSouls.TryGetValue(pawn, out int c);
            storedSouls[pawn] = c + count;
        }

        public bool ConsumeStoredSoul(Pawn pawn)
        {
            if (pawn == null || !storedSouls.TryGetValue(pawn, out int c) || c <= 0) return false;
            storedSouls[pawn] = c - 1;
            return true;
        }

        // Cursed Spirit Manipulation absorbed-curse roster (created on first access).
        public AbsorbedCurseData GetAbsorbedCurses(Pawn pawn)
        {
            if (pawn == null) return null;
            if (!absorbedCurses.TryGetValue(pawn, out AbsorbedCurseData data))
            {
                data = new AbsorbedCurseData();
                absorbedCurses[pawn] = data;
            }
            data.owner = pawn;
            data.MaterializeLegacy();
            return data;
        }

        public AbsorbedCurseData ExistingCurseRoster(Pawn pawn)
            => pawn != null && absorbedCurses.TryGetValue(pawn, out var roster) ? roster : null;

        public void TransferControlledCurse(Pawn previous, Pawn evolved)
        {
            foreach (var roster in absorbedCurses.Values)
            {
                int index = roster.deployed.IndexOf(previous);
                if (index >= 0) roster.deployed[index] = evolved;
            }
        }

        // Soul-mender insight: progress toward reversing cursed energy into a body
        // that is not your own, earned by tending the wounded.
        public int GetMenderInsight(Pawn pawn)
        {
            if (pawn == null) return 0;
            return menderInsight.TryGetValue(pawn, out int n) ? n : 0;
        }

        public void AddMenderInsight(Pawn pawn, int amount)
        {
            if (pawn == null || amount <= 0) return;
            menderInsight.TryGetValue(pawn, out int current);
            menderInsight[pawn] = current + amount;
        }

        // Forget a pawn's progress once it has been spent on the awakening.
        public void ClearMenderInsight(Pawn pawn)
        {
            if (pawn != null) menderInsight.Remove(pawn);
        }

        public AbilityTreeDef GetLastCastTree(Pawn pawn)
        {
            if (pawn == null) return null;
            return lastCastTree.TryGetValue(pawn, out AbilityTreeDef t) ? t : null;
        }

        public void SetLastCastTree(Pawn pawn, AbilityTreeDef tree)
        {
            if (pawn == null || tree == null) return;
            lastCastTree[pawn] = tree;
        }

        public int GetGradeXp(Pawn pawn)
        {
            if (pawn == null) return 0;
            return gradeXp.TryGetValue(pawn, out int xp) ? xp : 0;
        }

        public SorcererGrade GetGrade(Pawn pawn)
        {
            if (pawn != null && calamityUnlocked.Contains(pawn)) return SorcererGrade.Calamity;
            return JJK_GradeUtility.GradeForXp(GetGradeXp(pawn));
        }

        public void AddGradeXp(Pawn pawn, int amount)
        {
            if (pawn == null || amount <= 0 || !JJK_GradeUtility.IsSorcerer(pawn)) return;
            float factor = RimKaisenMod.Settings?.gradeXpFactor ?? 1f;
            amount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(amount * factor));
            SorcererGrade before = GetGrade(pawn);
            gradeXp.TryGetValue(pawn, out int current);
            gradeXp[pawn] = current + amount;
            SorcererGrade after = GetGrade(pawn);
            UpdateGradeHediff(pawn);
            if (after > before && pawn.Faction == Faction.OfPlayerSilentFail)
            {
                GameComponent_CampaignHistory.Instance?.Add(pawn, "JJK_RecordPromotion", JJK_GradeUtility.GradeLabels[(int)after]);
                Messages.Message("JJK_GradePromotion".Translate(pawn.LabelShortCap,
                        JJK_GradeUtility.GradeLabels[(int)after]),
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        // Quest hook: directly promote / unlock Calamity.
        public void UnlockCalamity(Pawn pawn)
        {
            if (pawn == null) return;
            if (!calamityUnlocked.Contains(pawn)) GameComponent_CampaignHistory.Instance?.Add(pawn, "JJK_RecordPromotion", JJK_GradeUtility.GradeLabels[5]);
            calamityUnlocked.Add(pawn);
            UpdateGradeHediff(pawn);
        }

        // Directly set a pawn's grade (used by quests and debug tools).
        public void SetGrade(Pawn pawn, SorcererGrade grade)
        {
            if (pawn == null || !JJK_GradeUtility.IsSorcerer(pawn)) return;
            if (grade > GetGrade(pawn)) GameComponent_CampaignHistory.Instance?.Add(pawn, "JJK_RecordPromotion", JJK_GradeUtility.GradeLabels[(int)grade]);
            if (grade >= SorcererGrade.Calamity)
            {
                calamityUnlocked.Add(pawn);
            }
            else
            {
                calamityUnlocked.Remove(pawn);
                gradeXp[pawn] = JJK_GradeUtility.CumulativeXpForTier[(int)grade];
            }
            UpdateGradeHediff(pawn);
        }

        public bool IncreaseGrade(Pawn pawn)
        {
            if (!JJK_GradeUtility.IsSorcerer(pawn)) return false;
            SorcererGrade g = GetGrade(pawn);
            if (g >= SorcererGrade.Calamity) return false;
            SetGrade(pawn, (SorcererGrade)((int)g + 1));
            return true;
        }

        public bool DecreaseGrade(Pawn pawn)
        {
            if (!JJK_GradeUtility.IsSorcerer(pawn)) return false;
            SorcererGrade g = GetGrade(pawn);
            if (g <= SorcererGrade.Grade4) return false;
            SetGrade(pawn, (SorcererGrade)((int)g - 1));
            return true;
        }

        // Binds the cursed-technique class level to the pawn's grade so that
        // higher-grade moves (acquireRequirement > minLevel) unlock with grade.
        public void SyncAbilityClassLevel(Pawn pawn)
        {
            CompAbilities comp = pawn?.GetComp<CompAbilities>();
            if (comp == null) return;
            AbilityClass ac = comp.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            if (ac == null) return;
            int lvl = (int)GetGrade(pawn);
            if (ac.level != lvl) ac.SetLevel(lvl);
        }

        public void UpdateGradeHediff(Pawn pawn)
        {
            if (pawn?.health == null) return;
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_SorcererGrade);

            if (!JJK_GradeUtility.IsSorcerer(pawn))
            {
                if (hediff != null) pawn.health.RemoveHediff(hediff);
                return;
            }

            if (hediff == null)
            {
                hediff = pawn.health.AddHediff(JJK_DefOf.JJK_SorcererGrade);
            }
            hediff.Severity = JJK_GradeUtility.SeverityForGrade(GetGrade(pawn));
            SyncAbilityClassLevel(pawn);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 == 0)
                foreach (var roster in absorbedCurses.Values.ToList()) roster.OrderDeployed();
            // Light touch: process the grade hediff for one home-map sorcerer per
            // tick on a rolling basis is overkill; instead refresh every ~5 sec.
            if (Find.TickManager.TicksGame % 300 != 0) return;
            foreach (var entry in absorbedCurses.ToList())
            {
                if (entry.Key == null || (!entry.Key.Dead && !entry.Key.Destroyed)) continue;
                Map releaseMap = entry.Key?.Corpse?.Map ?? Find.AnyPlayerHomeMap;
                if (releaseMap == null) continue;
                entry.Value.ReleaseOnOwnerLoss(releaseMap, entry.Key?.Corpse?.Position ?? releaseMap.Center);
            }
            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                var pawns = maps[i].mapPawns.FreeColonistsAndPrisonersSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn p = pawns[j];
                    if (JJK_GradeUtility.IsSorcerer(p)
                        && p.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_SorcererGrade) == null)
                    {
                        UpdateGradeHediff(p);
                    }
                }
            }
        }

        public override void ExposeData()
        {
            // Destroyed/absorbed/transfigured pawns will not be written by the game.
            // Do not serialize dangling keys and wait until after load to remove them.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                gradeXp.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                castPractice.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                calamityUnlocked.RemoveWhere(p => p == null || p.Destroyed);
                tenShadows.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                techniqueMastery.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                storedSouls.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                absorbedCurses.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                menderInsight.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
                lastCastTree.RemoveAll(kvp => kvp.Key == null || kvp.Key.Destroyed);
            }
            Scribe_Collections.Look(ref gradeXp, "gradeXp", LookMode.Reference, LookMode.Value,
                ref xpKeysWorking, ref xpValuesWorking);
            Scribe_Collections.Look(ref castPractice, "castPractice", LookMode.Reference, LookMode.Deep,
                ref castKeysWorking, ref castValuesWorking);
            Scribe_Collections.Look(ref calamityUnlocked, "calamityUnlocked", LookMode.Reference);
            Scribe_Collections.Look(ref tenShadows, "tenShadows", LookMode.Reference, LookMode.Deep,
                ref shadowKeysWorking, ref shadowValuesWorking);
            Scribe_Collections.Look(ref techniqueMastery, "techniqueMastery", LookMode.Reference, LookMode.Deep,
                ref masteryKeysWorking, ref masteryValuesWorking);
            Scribe_Collections.Look(ref storedSouls, "storedSouls", LookMode.Reference, LookMode.Value,
                ref soulKeysWorking, ref soulValuesWorking);
            Scribe_Collections.Look(ref absorbedCurses, "absorbedCurses", LookMode.Reference, LookMode.Deep,
                ref curseKeysWorking, ref curseValuesWorking);
            Scribe_Collections.Look(ref menderInsight, "menderInsight", LookMode.Reference, LookMode.Value,
                ref menderKeysWorking, ref menderValuesWorking);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gradeXp == null) gradeXp = new Dictionary<Pawn, int>();
                if (castPractice == null) castPractice = new Dictionary<Pawn, CastPracticeAllowance>();
                castPractice.RemoveAll(kvp => kvp.Key == null || kvp.Value == null);
                if (calamityUnlocked == null) calamityUnlocked = new HashSet<Pawn>();
                if (tenShadows == null) tenShadows = new Dictionary<Pawn, TenShadowsData>();
                if (techniqueMastery == null) techniqueMastery = new Dictionary<Pawn, TechniqueMasteryData>();
                if (storedSouls == null) storedSouls = new Dictionary<Pawn, int>();
                if (absorbedCurses == null) absorbedCurses = new Dictionary<Pawn, AbsorbedCurseData>();
                if (menderInsight == null) menderInsight = new Dictionary<Pawn, int>();
                // Drop any references that failed to resolve.
                gradeXp.RemoveAll(kvp => kvp.Key == null);
                calamityUnlocked.RemoveWhere(p => p == null);
                tenShadows.RemoveAll(kvp => kvp.Key == null || kvp.Value == null);
                techniqueMastery.RemoveAll(kvp => kvp.Key == null || kvp.Value == null);
                storedSouls.RemoveAll(kvp => kvp.Key == null);
                absorbedCurses.RemoveAll(kvp => kvp.Key == null || kvp.Value == null);
                menderInsight.RemoveAll(kvp => kvp.Key == null);
            }
        }
    }
}
