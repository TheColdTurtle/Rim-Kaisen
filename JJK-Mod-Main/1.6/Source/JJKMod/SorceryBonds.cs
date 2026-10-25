using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JJKMod
{
    public partial class Comp_CursedTool
    {
        public Pawn attunedPawn;
        public int wieldTicks, bondedHits, nextSignature, nextPracticeHit;
        public bool lossReported;
        public bool Attuned => attunedPawn != null && wieldTicks >= 180000 && bondedHits >= 20;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref attunedPawn, "attunedPawn");
            Scribe_Values.Look(ref wieldTicks, "wieldTicks");
            Scribe_Values.Look(ref bondedHits, "bondedHits");
            Scribe_Values.Look(ref nextSignature, "nextSignature");
            Scribe_Values.Look(ref nextPracticeHit, "nextPracticeHit");
            Scribe_Values.Look(ref lossReported, "lossReported");
        }
        public void Wield(Pawn wielder, int ticks)
        {
            if (wielder != attunedPawn || wielder.Downed || wielder.Dead) return;
            wieldTicks = System.Math.Min(180000, wieldTicks + ticks);
            if (lossReported)
            {
                lossReported = false;
                wielder.needs?.mood?.thoughts.memories.RemoveMemoriesOfDef(DefDatabase<ThoughtDef>.GetNamed("JJK_AttunedToolLost"));
            }
        }
        public void LandedHit(Pawn wielder, Pawn victim)
        {
            if (victim == null || !victim.HostileTo(wielder) || victim.Downed || victim.Dead) return;
            if (attunedPawn == null || attunedPawn.Dead)
            { attunedPawn = wielder; wieldTicks = bondedHits = nextPracticeHit = 0; lossReported = false; }
            if (attunedPawn != wielder) return;
            if (SorceryDevelopment.Now >= nextPracticeHit)
            { bondedHits = System.Math.Min(20, bondedHits + 1); nextPracticeHit = SorceryDevelopment.Now + 600; }
            if (!Attuned || SorceryDevelopment.Now < nextSignature) return;
            nextSignature = SorceryDevelopment.Now + 600;
            victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6, .25f, -1, wielder, weapon: parent.def));
        }
        private void Lost()
        {
            if (!Attuned || lossReported || attunedPawn.Dead) return;
            lossReported = true;
            attunedPawn.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("JJK_AttunedToolLost"));
        }
        public override void Notify_Unequipped(Pawn pawn) { base.Notify_Unequipped(pawn); if (pawn == attunedPawn) Lost(); }
        public override void PostDestroy(DestroyMode mode, Map previousMap) { Lost(); base.PostDestroy(mode, previousMap); }
        public override string CompInspectStringExtra() => attunedPawn == null ? "JJK_DeepToolUnbound".Translate()
            : "JJK_DeepToolProgress".Translate(attunedPawn.LabelShortCap, (wieldTicks / 60000f).ToString("F1"), bondedHits)
                + (Attuned ? "\n" + "JJK_DeepToolEffect".Translate().ToString() : "");
    }

    public static class ShadowInheritance
    {
        // Gameplay adaptations: a fixed compatibility table, no Mahoraga, dogs,
        // expendable swarms or domain summons. A donor and heir can each be used once.
        public static readonly string[,] Pairs = {
            { "Toad", "Nue", "JJK_InheritGrip" },
            { "GreatSerpent", "Nue", "JJK_InheritFang" },
            { "Nue", "GreatSerpent", "JJK_InheritSpark" },
            { "RoundDeer", "TigerFuneral", "JJK_InheritVigor" },
            { "TigerFuneral", "PiercingOx", "JJK_InheritFang" }
        };
        public static ShikigamiDef Shadow(string name) => DefDatabase<ShikigamiDef>.GetNamed("JJK_Shadow_" + name);
        public static bool CanInherit(Pawn p, int pair)
        {
            if (p == null || pair < 0 || pair >= Pairs.GetLength(0)) return false;
            var tree = DefDatabase<TaranMagicFramework.AbilityTreeDef>.GetNamed("JJK_CT_TenShadows");
            if (p.GetComp<TaranMagicFramework.CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques)?.UnlockedTrees.Contains(tree) != true
                || GameComponent_JJK.Instance.GetTechniqueXp(p, tree) < 500) return false;
            var r = SorceryDevelopment.State(p); var data = GameComponent_JJK.Instance.GetTenShadows(p);
            var donor = Shadow(Pairs[pair, 0]); var heir = Shadow(Pairs[pair, 1]);
            return r.donors.Count < 2 && !r.donors.Contains(donor) && !r.heirs.Contains(heir)
                && !r.heirs.Contains(donor) && !r.donors.Contains(heir)
                && data.GetState(donor) == ShikigamiState.Lost && data.GetState(heir) == ShikigamiState.Available;
        }
        public static bool Inherit(Pawn p, int pair)
        {
            if (!CanInherit(p, pair)) return false;
            var r = SorceryDevelopment.State(p); r.donors.Add(Shadow(Pairs[pair, 0])); r.heirs.Add(Shadow(Pairs[pair, 1]));
            // Existing allies receive it too; a recall/recast is not required.
            foreach (var map in Find.Maps) foreach (var summon in map.mapPawns.AllPawnsSpawned.ToList()) Apply(summon);
            return true;
        }
        public static void Apply(Pawn summon)
        {
            var bond = summon.health?.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_ShikigamiBond) as Hediff_ShikigamiBond;
            if (bond == null || bond.tamingInstance || bond.owner == null || bond.shikigami == null) return;
            var r = GameComponent_SorceryDevelopment.Instance?.For(bond.owner, false);
            if (r == null) return;
            int i = r.heirs.IndexOf(bond.shikigami);
            if (i < 0 || i >= r.donors.Count) return;
            for (int j = 0; j < Pairs.GetLength(0); j++)
                if (r.donors[i] == Shadow(Pairs[j, 0]) && bond.shikigami == Shadow(Pairs[j, 1]) && !SorceryDevelopment.Has(summon, Pairs[j, 2]))
                    summon.health.AddHediff(DefDatabase<HediffDef>.GetNamed(Pairs[j, 2]));
        }
    }
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), nameof(Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget))]
    public static class Patch_DeepWeaponHit
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target, DamageWorker.DamageResult __result)
        {
            if (__result?.totalDamageDealt <= 0 || __result == null || !(target.Thing is Pawn victim) || victim.Dead) return;
            var p = __instance.CasterPawn;
            __instance.EquipmentSource?.TryGetComp<Comp_CursedTool>()?.LandedHit(p, victim);
            if (SorceryDevelopment.Has(p, "JJK_InheritGrip") || SorceryDevelopment.Has(p, "JJK_InheritSpark"))
            {
                var h = p.health.hediffSet.hediffs.First(x => x.def.defName == "JJK_InheritGrip" || x.def.defName == "JJK_InheritSpark") as Hediff_InheritedShadow;
                if (h != null && h.nextEffect <= SorceryDevelopment.Now)
                { h.nextEffect = SorceryDevelopment.Now + 600; victim.stances.stunner.StunFor(45, p, false, true); }
            }
        }
    }
    public class Hediff_InheritedShadow : HediffWithComps
    {
        public int nextEffect;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref nextEffect, "nextEffect"); }
    }
}
