using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Bind the cursed-technique class level to the pawn's sorcerer grade when the
    // class is initialized (mirrors how the Naruto mod binds Taijutsu level to the
    // Melee skill), so grade-gated moves (acquireRequirement minLevel) unlock with
    // grade. Player sorcerers use their tracked grade; a non-player sorcerer uses its
    // pawnkind's authored grade (PawnKindSorcererExtension), so a "Special" elite
    // really fields Special-grade moves. Falls back to a combat-power estimate only
    // for non-player sorcerers with no authored grade.
    [HarmonyPatch(typeof(AbilityClass), "Init")]
    public static class Patch_AbilityClass_Init
    {
        public static void Postfix(AbilityClass __instance)
        {
            if (__instance?.def != JJK_DefOf.JJK_CursedTechniques) return;
            Pawn pawn = __instance.pawn;
            if (pawn == null) return;

            int lvl;
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                lvl = (int)(GameComponent_JJK.Instance?.GetGrade(pawn) ?? SorcererGrade.Grade4);
            }
            else
            {
                PawnKindSorcererExtension ext = pawn.kindDef?.GetModExtension<PawnKindSorcererExtension>();
                if (ext != null)
                {
                    lvl = (int)ext.grade;
                }
                else
                {
                    float cp = pawn.kindDef?.combatPower ?? 70f;
                    lvl = Mathf.Clamp(Mathf.RoundToInt(cp / 80f), 0, 4);
                }
            }
            if (__instance.level != lvl) __instance.SetLevel(lvl);
        }
    }

    // On a cursed-technique cast: build the caster's mastery in that technique
    // (all sorcerers, used for domain clashes), remember which technique they just
    // used (so a follow-up kill credits it), and award grade XP (player only).
    // Cast opens the target picker; RegisterCast runs when energy is actually spent.
    [HarmonyPatch(typeof(Ability), "RegisterCast")]
    public static class Patch_Ability_Cast
    {
        private const int XpPerCast = 3;
        private const int MasteryPerCast = 2;

        public static void Postfix(Ability __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            Pawn pawn = __instance?.pawn;
            if (pawn == null) return;
            if (__instance.abilityResource?.def != JJK_DefOf.JJK_CursedEnergy) return;
            CombatPresentation.Cast(__instance);
            pawn.Map?.GetComponent<MapComponent_ResidualEnergy>()?.Record(pawn, __instance.def?.abilityTrees?.FirstOrDefault());
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return;

            AbilityTreeDef tree = __instance.def?.abilityTrees?.FirstOrDefault();
            // Puppet orders spend core charge or transfer CE before RegisterCast;
            // their TMF tier is intentionally free. Other free controls earn no XP.
            bool reward = gc.TryRewardCastPractice(pawn,
                __instance is Ability_PuppetOrder ? 1f : __instance.EnergyCost);
            if (tree != null)
            {
                if (reward) gc.AddTechniqueXp(pawn, tree, MasteryPerCast);
                gc.SetLastCastTree(pawn, tree);
            }

            if (reward && pawn.Faction != null && pawn.Faction.IsPlayer) gc.AddGradeXp(pawn, XpPerCast);

            // Cursed speech cuts at the speaker's tongue; heavier words strain more.
            var speech = __instance.def?.GetModExtension<CursedSpeechExtension>();
            if (speech != null) CursedSpeechUtil.StrainVoice(pawn, speech.voiceStrain);
        }
    }

    // Award sorcerer grade XP when a player sorcerer lands a kill (Section 6.2).
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out bool __state)
        {
            // Death can end a factionless curse's manhunter state. Capture actual
            // hostility first; captive and friendly kills cannot farm progression.
            Pawn killer = dinfo?.Instigator as Pawn;
            __state = killer != null && killer != __instance && !__instance.IsPrisoner
                && __instance.HostileTo(killer);
        }

        public static void Postfix(Pawn __instance, DamageInfo? dinfo, bool __state)
        {
            // Mahoraga's death may have been cancelled by the adaptation patch.
            if (!__instance.Dead) return;

            // A dying shikigami: complete taming, or lose it permanently.
            HandleShikigamiDeath(__instance);
            // A dying domain summon (fish / Chimera shadows) just vanishes.
            VanishDomainSummon(__instance);
            // An exorcised cursed spirit dissipates, leaving cursed residue.
            ExorciseCursedSpirit(__instance);

            if (!dinfo.HasValue) return;
            Pawn killer = dinfo.Value.Instigator as Pawn;
            if (killer == null || killer == __instance) return;
            if (!JJK_GradeUtility.IsSorcerer(killer)) return;
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return;

            // Cursed-tool on-kill effects (Hiten's refund, Black Sword's frenzy).
            ApplyCursedToolKillEffects(killer, dinfo.Value, gc);

            if (!__state) return;
            int kxp = JJK_Utils.GradeXpForKill(__instance);

            // Technique mastery for the technique the killer was using (any sorcerer).
            AbilityTreeDef usedTree = gc.GetLastCastTree(killer);
            if (usedTree != null) gc.AddTechniqueXp(killer, usedTree, kxp);

            // Grade XP: player sorcerers killing hostiles only.
            if (killer.Faction != null && killer.Faction.IsPlayer)
            {
                gc.AddGradeXp(killer, kxp);
            }
        }

        // On-kill hooks for named cursed tools. Reads the weapon off the killing
        // DamageInfo and confirms the killer still wields it.
        private static void ApplyCursedToolKillEffects(Pawn killer, DamageInfo dinfo, GameComponent_JJK gc)
        {
            ThingDef weaponDef = dinfo.Weapon;
            if (weaponDef == null || killer.equipment?.Primary?.def != weaponDef) return;
            CompProperties_CursedTool props = weaponDef.GetCompProperties<CompProperties_CursedTool>();
            if (props == null) return;

            // Hiten: a strong enough wielder's reserves replenish with every kill.
            if (props.energyOnKill > 0f && EffectiveGrade(killer, gc) >= props.energyOnKillMinGrade)
            {
                AbilityResource res = killer.GetComp<CompAbilities>()
                    ?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
                if (res != null)
                {
                    res.energy = Mathf.Min(res.MaxEnergy, res.energy + props.energyOnKill);
                    if (killer.Spawned)
                    {
                        MoteMaker.ThrowText(killer.DrawPos, killer.Map,
                            "JJK_TalismanRestored".Translate(Mathf.RoundToInt(props.energyOnKill)));
                    }
                }
            }

            // Black Sword: the kill whips every shikigami bonded to the wielder
            // into renewed ferocity for a while.
            if (props.shikigamiFrenzyHediff != null && killer.Map != null)
            {
                foreach (Pawn p in killer.Map.mapPawns.AllPawnsSpawned)
                {
                    var bond = p.health?.hediffSet
                        ?.GetFirstHediffOfDef(JJK_DefOf.JJK_ShikigamiBond) as Hediff_ShikigamiBond;
                    if (bond == null || bond.owner != killer || bond.tamingInstance) continue;

                    Hediff frenzy = p.health.hediffSet.GetFirstHediffOfDef(props.shikigamiFrenzyHediff);
                    if (frenzy == null) frenzy = p.health.AddHediff(props.shikigamiFrenzyHediff);
                    HediffComp_Disappears dis = frenzy.TryGetComp<HediffComp_Disappears>();
                    if (dis != null) dis.ticksToDisappear = Mathf.Max(dis.ticksToDisappear, props.shikigamiFrenzyTicks);
                }
            }
        }

        // A player sorcerer's grade is tracked; a non-player one uses its
        // pawnkind's authored grade (same rule as Patch_AbilityClass_Init).
        private static SorcererGrade EffectiveGrade(Pawn pawn, GameComponent_JJK gc)
        {
            if (pawn.Faction != null && pawn.Faction.IsPlayer) return gc.GetGrade(pawn);
            return pawn.kindDef?.GetModExtension<PawnKindSorcererExtension>()?.grade
                   ?? gc.GetGrade(pawn);
        }

        private static void HandleShikigamiDeath(Pawn victim)
        {
            Hediff_ShikigamiBond bond = victim?.health?.hediffSet
                ?.GetFirstHediffOfDef(JJK_DefOf.JJK_ShikigamiBond) as Hediff_ShikigamiBond;
            if (bond == null) return;

            // Update the owner's roster state.
            if (bond.owner != null && bond.shikigami != null)
            {
                TenShadowsData data = GameComponent_JJK.Instance?.GetTenShadows(bond.owner);
                if (data != null)
                {
                    bool ownerVisible = bond.owner.Faction != null && bond.owner.Faction.IsPlayer;
                    if (bond.tamingInstance)
                    {
                        // Defeated the hostile instance -> the shadow is now tamed.
                        data.SetState(bond.shikigami, ShikigamiState.Available);
                        if (ownerVisible)
                        {
                            Messages.Message("JJK_ShikigamiTamed".Translate(bond.owner.LabelShortCap, bond.shikigami.LabelCap),
                                bond.owner, MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                    else if (bond.shikigami.losableOnDeath)
                    {
                        // A tamed shikigami died in service -> lost forever.
                        // (Expendable swarm members like rabbits are exempt.)
                        if (data.GetState(bond.shikigami) != ShikigamiState.Lost)
                            GameComponent_CampaignHistory.Instance?.Add(bond.owner, "JJK_RecordShadow", bond.shikigami.LabelCap);
                        data.SetState(bond.shikigami, ShikigamiState.Lost);
                        if (ownerVisible)
                        {
                            Messages.Message("JJK_ShikigamiLostMessage".Translate(bond.owner.LabelShortCap, bond.shikigami.LabelCap),
                                bond.owner, MessageTypeDefOf.NegativeEvent, false);
                        }
                    }
                }
            }

            // The shikigami returns to the shadows rather than leaving a corpse.
            Corpse corpse = victim.Corpse;
            if (corpse != null && corpse.Spawned)
            {
                if (corpse.Map != null) FleckMaker.ThrowSmoke(corpse.DrawPos, corpse.Map, 2f);
                corpse.Destroy();
            }
        }

        // Temporary domain summons (fish, Chimera Shadow Garden shadows) carry no
        // shikigami bond, so HandleShikigamiDeath ignores them. They still vanish
        // on death rather than leaving a corpse on the field.
        private static void VanishDomainSummon(Pawn victim)
        {
            Corpse corpse = victim?.Corpse;
            if (corpse == null || !corpse.Spawned) return;
            MapComponent_DomainManager mgr = corpse.Map?.GetComponent<MapComponent_DomainManager>();
            if (mgr == null || !mgr.IsDomainSummon(victim)) return;
            FleckMaker.ThrowSmoke(corpse.DrawPos, corpse.Map, 2f);
            corpse.Destroy();
        }

        // An exorcised curse dissipates into cursed residue rather than leaving a
        // corpse - the lore-accurate "exorcism" (the residue is the raw cursed
        // energy that gave it form). The residue is the reward for the kill.
        private static void ExorciseCursedSpirit(Pawn victim)
        {
            if (!JJK_Utils.IsCursedSpirit(victim)) return;

            Map map;
            IntVec3 pos;
            Corpse corpse = victim?.Corpse;
            if (corpse != null && corpse.Spawned)
            {
                map = corpse.Map;
                pos = corpse.Position;
                if (map != null) FleckMaker.ThrowSmoke(corpse.DrawPos, map, 2.5f);
                corpse.Destroy();
            }
            else
            {
                // No corpse: the kill obliterated the body (a big explosion, or one of
                // this mod's own AoE finishers like Hollow Purple). Fall back to where
                // the victim died, otherwise killing a curse with your strongest attack
                // would silently forfeit the residue - and a boss's technique remnant,
                // which is the only way to learn its innate technique.
                map = victim?.MapHeld;
                pos = victim?.PositionHeld ?? IntVec3.Invalid;
                if (map == null || !pos.IsValid || !pos.InBounds(map)) return;
            }

            int amount = JJK_Utils.ResidueForExorcism(victim);
            if (map != null && amount > 0 && JJK_DefOf.JJK_CursedResidue != null)
            {
                Thing residue = ThingMaker.MakeThing(JJK_DefOf.JJK_CursedResidue);
                residue.stackCount = amount;
                GenPlace.TryPlaceThing(residue, pos, map, ThingPlaceMode.Near);
            }

            // Boss curses leave behind a technique remnant.
            ThingDef drop = victim.def.GetModExtension<CursedSpiritExtension>()?.bossDrop;
            if (map != null && drop != null)
            {
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(drop), pos, map, ThingPlaceMode.Near);
            }
        }
    }

    internal static class MahoragaUtil
    {
        public static Comp_MahoragaAdaptation CompOf(Pawn p)
        {
            if (p == null || p.def != JJK_DefOf.JJK_Mahoraga) return null;
            return p.TryGetComp<Comp_MahoragaAdaptation>();
        }
    }

    // Resistance uses the originating phenomenon; DamageDef still controls armor/wounds.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PreApplyDamage))]
    public static class Patch_MahoragaAdaptation
    {
        public static bool Prefix(Pawn_HealthTracker __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            Comp_MahoragaAdaptation comp = MahoragaUtil.CompOf(__instance.pawn);
            if (comp == null || dinfo.Def == null) return true;

            string phenomenon = MahoragaPhenomena.Resolve(dinfo);
            if (comp.IsImmune(phenomenon))
            {
                absorbed = true; // fully adapted -> negated
                return false;
            }
            if (comp.GetLevel(phenomenon) >= 1)
            {
                dinfo.SetAmount(dinfo.Amount * comp.DamageFactor(phenomenon));
            }
            return true;
        }
    }

    // Surviving exposure starts one seven-second turn, without accelerating for burst chunks.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    public static class Patch_MahoragaPostDamage
    {
        public static void Postfix(Pawn_HealthTracker __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f) return;
            Pawn p = __instance.pawn;
            if (p == null || p.Dead) return;
            MahoragaUtil.CompOf(p)?.NotifyHit(MahoragaPhenomena.Resolve(dinfo));
        }
    }

    // Basic physical phenomena cannot kill after one spin. Complex techniques
    // remain lethal during partial adaptation unless the race tuning opts in.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_MahoragaDeathBlock
    {
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            Comp_MahoragaAdaptation comp = MahoragaUtil.CompOf(__instance);
            if (comp == null || !dinfo.HasValue || dinfo.Value.Def == null) return true;

            string phenomenon = MahoragaPhenomena.Resolve(dinfo.Value);
            if (comp.BlocksDeath(phenomenon))
            {
                comp.FullRegen();
                if (PawnUtility.ShouldSendNotificationAbout(__instance))
                {
                    Messages.Message("JJK_MahoragaSurvived".Translate(__instance.LabelShortCap, MahoragaPhenomena.Label(phenomenon)),
                        __instance, MessageTypeDefOf.NegativeEvent, false);
                }
                return false; // cancel death
            }
            return true;
        }
    }

    // Dismiss command on player-owned summons carrying a shikigami bond (Ryu,
    // Garuda, the Ten Shadows, crow flocks...). Removing the bond fires
    // Hediff_ShikigamiBond.PostRemoved, which returns the summon to the shadows
    // cleanly - the same path as a timed expiry, so nothing counts as a death.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_DismissSummon
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (__instance == null || __instance.Dead) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;
            var bond = __instance.health?.hediffSet
                ?.GetFirstHediffOfDef(JJK_DefOf.JJK_ShikigamiBond) as Hediff_ShikigamiBond;
            if (bond == null || bond.tamingInstance) yield break;

            Pawn summon = __instance;
            yield return new Command_Action
            {
                defaultLabel = "JJK_DismissSummon".Translate(),
                defaultDesc = "JJK_DismissSummonDesc".Translate(summon.LabelShortCap),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => summon.health.RemoveHediff(bond)
            };
        }
    }

    // Dev-only: a per-technique "+mastery" gizmo on each sorcerer, shown only in
    // god mode. Lets you pump a technique's mastery XP to see domains strengthen
    // and to set up domain clashes. One button per cursed technique the pawn knows.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_DevMastery
    {
        private const int MasteryPerClick = 500;

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;

            if (!DebugSettings.godMode || __instance == null) yield break;
            if (!JJK_GradeUtility.IsSorcerer(__instance)) yield break;
            AbilityClass ac = __instance.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            if (ac?.UnlockedTrees == null) yield break;
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) yield break;

            foreach (AbilityTreeDef tree in ac.UnlockedTrees)
            {
                AbilityTreeDef captured = tree;
                yield return new Command_Action
                {
                    defaultLabel = "DEV: +mastery\n" + captured.label,
                    defaultDesc = "Add " + MasteryPerClick + " mastery XP in " + captured.label
                        + ".\nCurrent XP: " + gc.GetTechniqueXp(__instance, captured)
                        + "\nEffective (with grade): " + gc.GetEffectiveMastery(__instance, captured),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => gc.AddTechniqueXp(__instance, captured, MasteryPerClick)
                };
            }
        }
    }

    // Kenjaku strikes when you are comfortable. While he is the storyteller, every
    // threat's points are scaled by the colony's average mood: a thriving, content
    // colony faces markedly bigger raids; a struggling one gets a measure of slack.
    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow))]
    public static class Patch_KenjakuMoodEscalation
    {
        public static void Postfix(IIncidentTarget target, ref float __result)
        {
            if (Find.Storyteller?.def != JJK_IncidentDefOf.JJK_Kenjaku) return;
            float mood = AverageColonistMood(target);
            if (mood < 0f) return;
            // mood 0.25 -> x0.8 (slack), 0.9 -> x1.5 (he presses his advantage).
            float m = Mathf.Clamp(mood, 0.25f, 0.9f);
            __result *= Mathf.Lerp(0.8f, 1.5f, (m - 0.25f) / 0.65f);
        }

        private static float AverageColonistMood(IIncidentTarget target)
        {
            if (!(target is Map map)) return -1f;
            var colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists == null || colonists.Count == 0) return -1f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                Need_Mood mood = colonists[i].needs?.mood;
                if (mood != null) { sum += mood.CurLevelPercentage; n++; }
            }
            return n == 0 ? -1f : sum / n;
        }
    }

    // Cursed spirits are born hostile. Any factionless curse that spawns - dev
    // console, incident, or otherwise - immediately enters a permanent manhunter
    // state, so curses always attack rather than milling about as wild animals.
    // Curses that belong to a faction (curse-user raid beasts) are left alone so
    // they fight for their side instead of turning on their handlers.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_CurseManhunter
    {
        public static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || __instance == null) return;
            if (__instance.Dead || !__instance.Spawned || __instance.Faction != null) return;
            if (!JJK_Utils.IsCursedSpirit(__instance)) return;
            if (__instance.MentalStateDef == MentalStateDefOf.ManhunterPermanent) return;
            __instance.mindState?.mentalStateHandler?.TryStartMentalState(
                MentalStateDefOf.ManhunterPermanent, null, true);
        }
    }

    // A vanilla door holds itself open whenever an item or pawn sits in its cell
    // (so it never crushes anything). A domain barrier is a magic forcefield, not a
    // mechanical door: it must stay sealed even with something underneath it.
    // Without this, an item left on a barrier cell forces the "door" open and lets
    // ANY pawn - not just a heavenly-restriction phaser - walk straight through.
    [HarmonyPatch(typeof(Building_Door), "get_BlockedOpenMomentary")]
    public static class Patch_DomainBarrier_BlockedOpenMomentary
    {
        public static bool Prefix(Building_Door __instance, ref bool __result)
        {
            if (__instance is DomainBarrierDoor)
            {
                __result = false;
                return false; // skip vanilla: never auto-hold-open
            }
            return true;
        }
    }

    // Roll random cursed genes onto newly generated humanlike pawns (Section 9.3).
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_GeneratePawn
    {
        public static void Postfix(Pawn __result)
        {
            if (__result == null) return;
            JJK_Utils.TryRollCursedGenes(__result);
        }
    }
}
