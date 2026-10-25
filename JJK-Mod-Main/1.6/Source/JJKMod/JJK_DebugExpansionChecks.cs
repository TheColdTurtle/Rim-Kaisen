using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public static class JJK_DebugExpansionChecks
    {
        private static Pawn Human(string name)
        {
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            p.Name = new NameSingle(name);
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(JJK_DebugAdvancedPuppetChecks.Clear(Find.CurrentMap), Find.CurrentMap, 3), Find.CurrentMap);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.GetComp<CompAbilities>().RecheckAbilities();
            GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Special);
            var ce = p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy); ce.energy = ce.MaxEnergy;
            return p;
        }
        private static Pawn Curse(string kind = "JJK_Curse_Grade4_Kind")
        {
            Pawn p = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind));
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(JJK_DebugAdvancedPuppetChecks.Clear(Find.CurrentMap), Find.CurrentMap, 4), Find.CurrentMap);
            return p;
        }
        private static void Learn(Pawn p, string name)
        {
            var def = DefDatabase<AbilityDef>.GetNamed(name); var ac = JujutsuTraining.Class(p);
            ac.UnlockTree(def.abilityTrees[0]); ac.LearnAbility(def, false);
        }
        [DebugAction("Rim-Kaisen", "Run six-feature regression checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass = 0; var fail = new List<string>(); var cleanup = new List<Thing>();
            Action<bool, string> check = (ok, name) => { if (ok) pass++; else fail.Add(name); Log.Message("[JJK Expansion QA] " + (ok ? "PASS " : "FAIL ") + name); };
            var gc = GameComponent_Jujutsu.Instance; var progress = GameComponent_JJK.Instance;
            try
            {
                Pawn p = Human("Expansion tester"); cleanup.Add(p);
                var roster = progress.GetAbsorbedCurses(p);
                Pawn first = Curse(), second = Curse(); cleanup.Add(first); cleanup.Add(second);
                first.Name = new NameSingle("Captured individual QA");
                var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, first, first.RaceProps.body.corePart); injury.Severity = 2f; first.health.AddHediff(injury);
                string identity = first.ThingID;
                check(roster.Capture(first) && !first.Spawned && !first.Destroyed && roster.Count == 1, "capture stores the living individual");
                check(roster.Capture(second) && roster.Count == 2, "multiple captures are stored independently");
                roster.releaseId = first.ThingID;
                Pawn released = roster.Release(p.Position, p.Map);
                check(released == first && released.ThingID == identity && released.health.hediffSet.hediffs.Contains(injury), "selected release preserves identity and injury");
                Pawn enemyCurse = Curse(); cleanup.Add(enemyCurse);
                enemyCurse.Position = CellFinder.RandomClosewalkCellNear(first.Position, first.Map, 2);
                roster.OrderDeployed();
                check(first.CurJob?.def == JobDefOf.AttackMelee && first.CurJob.targetA.Pawn != null,
                    "released curse receives a real combat job against nearby hostiles");
                enemyCurse.Destroy();
                check(roster.deployed.Contains(first) && roster.Capture(first), "released individual can be recalled");
                roster.SelectSacrifice(first, false);
                check(roster.SelectedCount == 1, "Uzumaki selection excludes the protected individual");
                float power = roster.ConsumeSelected(out int count);
                check(count == 1 && power == second.kindDef.combatPower && second.Destroyed && !first.Destroyed && roster.Count == 1, "Uzumaki consumes only selected curses");
                roster.kinds.Add(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade4_Kind")); roster.MaterializeLegacy();
                check(roster.kinds.Count == 0 && roster.Count == 2, "legacy kind-only roster migrates once");
                roster.MaterializeLegacy(); check(roster.Count == 2, "legacy migration cannot duplicate curses");
                cleanup.AddRange(roster.Pawns);

                Learn(p, "JJK_Speech_DontMove");
                var plan = gc.Plan(p); plan.tree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech"); plan.enabled = true;
                int before = progress.GetTechniqueXp(p, plan.tree); float energy = p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy;
                int gain = JujutsuTraining.Complete(plan);
                check(gain > 0 && progress.GetTechniqueXp(p, plan.tree) == before + gain, "completed practice adds mastery");
                check(p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy == energy - 10, "practice spends exactly 10 CE");
                progress.AddTechniqueXp(p, plan.tree, 1500); int reduced = JujutsuTraining.Complete(plan);
                check(reduced > 0 && reduced < gain, "experienced techniques get diminishing practice returns");
                plan.dailyXp = 80; check(JujutsuTraining.Complete(plan) == 0, "daily practice cap blocks extra progress");
                plan.dailyXp = 0;
                Pawn mentor = Human("Mentor tester"); cleanup.Add(mentor); mentor.Position = CellFinder.RandomClosewalkCellNear(p.Position, p.Map, 2);
                Learn(mentor, "JJK_Domain_Simple"); plan.mentor = mentor;
                plan.tree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_Domain_SimpleTree"); plan.lesson = DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple");
                for (int i = 0; i < 5; i++) { plan.dailyXp = 0; JujutsuTraining.Complete(plan); }
                check(JujutsuTraining.Class(p).GetLearnedAbility(DefDatabase<AbilityDef>.GetNamed("JJK_Domain_Simple")) != null, "supervised lessons teach a known learned skill");
                plan.lesson = DefDatabase<AbilityDef>.GetNamed("JJK_Dismantle");
                check(!JujutsuTraining.Valid(plan, false), "mentorship cannot grant innate technique genes"); plan.enabled = false;

                foreach (var gene in p.genes.GenesListForReading.Where(g => g.def.defName == "JJK_CT_Gene_Shrine").ToList()) p.genes.RemoveGene(gene);
                var influence = p.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence); influence.Severity = 0.3f;
                var vessel = influence.TryGetComp<HediffComp_SukunaVessel>();
                check(vessel.Bargain() && p.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine")), "bargain grants temporary Shrine access");
                check(!vessel.Bargain(), "duplicate bargain is blocked");
                vessel.Suppress();
                check(vessel.debt && !vessel.Possessed && !p.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine")), "suppression removes borrowed power but retains debt");
                check(!vessel.BeginPossession(), "suppression blocks a new possession");
                vessel.suppressedUntil = 0; vessel.TickState();
                check(vessel.Possessed && p.MentalStateDef == MentalStateDefOf.Berserk && !vessel.debt, "expired suppression allows debt collection");
                vessel.EndPossession();
                check(!vessel.Possessed && !p.InMentalState && !p.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine")), "possession cleanup removes temporary powers and berserk");
                check(!JujutsuTraining.Class(p).UnlockedTrees.Contains(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine")),
                    "temporary Shrine tree becomes unavailable after possession ends");
                p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine"), true); vessel.nextBargain = 0;
                check(vessel.Bargain(), "existing Shrine bearer can bargain"); vessel.Suppress();
                check(p.genes.HasActiveGene(DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine")), "cleanup preserves a pre-existing Shrine gene");

                Faction school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
                var standing = gc.Standing(school); int oldRep = standing.reputation, oldNext = standing.nextOfferTick;
                check(!gc.CanAccept(school, p.Map, 3, ContractKind.Boss), "school reputation gates elite boss contracts");
                foreach (ContractKind kind in Enum.GetValues(typeof(ContractKind)))
                {
                    standing.reputation = 60; standing.nextOfferTick = 0;
                    var contract = gc.Accept(school, p.Map, kind == ContractKind.Boss ? 3 : 1, kind);
                    if (ColonyMissions.Special(kind))
                    {
                        check(contract == null, kind + " requires an expedition instead of a local encounter");
                        continue;
                    }
                    check(contract != null && contract.Remaining > 0, kind + " creates an actual encounter");
                    if (contract == null) continue;
                    cleanup.AddRange(contract.threats);
                    check(!gc.CanAccept(school, p.Map, 1, ContractKind.Infestation), kind + " prevents duplicate active assignments");
                    foreach (var threat in contract.threats) threat.Destroy();
                    if (contract.rescue != null)
                    {
                        cleanup.Add(contract.rescue);
                        foreach (var h in contract.rescue.health.hediffSet.hediffs.ToList()) contract.rescue.health.RemoveHediff(h);
                        contract.rescue.Position = p.Position; contract.rescue.health.Notify_HediffChanged(null);
                        p.Map.areaManager.Home[p.Position] = true;
                    }
                    if (contract.artifact != null) { cleanup.Add(contract.artifact); contract.artifact.Position = p.Position; p.Map.areaManager.Home[p.Position] = true; }
                    gc.Evaluate(contract);
                    check(contract.state == ContractState.Ready && gc.Claim(contract), kind + " objective pays reward once");
                    check(!gc.Claim(contract), kind + " cannot duplicate payout");
                }
                standing.nextOfferTick = 0;
                var expired = gc.Accept(school, p.Map, 1, ContractKind.Infestation); cleanup.AddRange(expired.threats);
                expired.expires = Find.TickManager.TicksGame - 1; int repBefore = standing.reputation; gc.Evaluate(expired);
                check(expired.state == ContractState.Failed && standing.reputation == repBefore - 5, "expired contract fails and applies reputation penalty");
                standing.reputation = oldRep; standing.nextOfferTick = oldNext;

                foreach (string kind in new[] { "Jogo", "Hanami", "Dagon", "Mahito" })
                {
                    Pawn boss = Curse("JJK_Boss_" + kind + "_Kind"); cleanup.Add(boss);
                    var pattern = boss.TryGetComp<CompBossPattern>();
                    p.Position = CellFinder.RandomClosewalkCellNear(boss.Position, boss.Map, 1);
                    check(pattern.Begin(p) && pattern.Charging && pattern.DangerCells().Count > 0, kind + " warns before its committed attack");
                    p.Position = p.Map.AllCells.First(c => c.Standable(p.Map) && c.DistanceToSquared(boss.Position) > 900);
                    float health = p.health.summaryHealth.SummaryHealthPercent;
                    pattern.Resolve();
                    check(!p.Destroyed && p.health.summaryHealth.SummaryHealthPercent == health, kind + " attack is avoidable by moving away");
                    check(pattern.Busy && boss.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_BossOpening")), kind + " exposes a recovery opening");
                    Pawn stationary = Human("Stationary target QA"); cleanup.Add(stationary);
                    // This assertion measures the boss's committed hit area, not
                    // the random starting outfit's chance to absorb that hit.
                    stationary.apparel.DestroyAll();
                    stationary.Position = CellFinder.RandomClosewalkCellNear(boss.Position, boss.Map, 1);
                    if (kind == "Mahito" && !stationary.story.traits.HasTrait(JJK_DefOf.JJK_SoulObserver))
                        stationary.story.traits.GainTrait(new Trait(JJK_DefOf.JJK_SoulObserver));
                    pattern.recoveryUntil = 0;
                    bool began = pattern.Begin(stationary);
                    string hitContext = "begin="+began+" bossDowned="+boss.Downed+" origin="+pattern.origin+" aim="+pattern.aim
                        +" target="+stationary.Position+" inside="+pattern.DangerCells().Contains(stationary.Position)
                        +" los="+GenSight.LineOfSight(pattern.origin,stationary.Position,boss.Map)+" hostile="+stationary.HostileTo(boss);
                    pattern.Resolve();
                    // A destroyed hand/finger leaves MissingBodyPart instead of
                    // retaining its injury. Either outcome proves the committed hit.
                    bool damaged = stationary.health.hediffSet.hediffs.Any(h=>h is Hediff_Injury || h is Hediff_MissingPart);
                    if(!damaged)
                        Log.Message("[JJK Expansion QA] hit diagnostic "+kind+": "+hitContext+" health="+string.Join(",",stationary.health.hediffSet.hediffs.Select(h=>h.def.defName)));
                    check(!stationary.Destroyed && damaged,
                        kind + " hits a stationary target; soul awareness resists Mahito transfiguration");
                    if (kind == "Mahito")
                    {
                        Pawn unaware = Human("Unaware target QA"); cleanup.Add(unaware);
                        foreach (var t in unaware.story.traits.allTraits.Where(t => t.def == JJK_DefOf.JJK_SoulObserver).ToList()) unaware.story.traits.RemoveTrait(t);
                        unaware.Position = CellFinder.RandomClosewalkCellNear(boss.Position, boss.Map, 1);
                        int soulsBefore = boss.Map.mapPawns.AllPawnsSpawned.Count(t => t.kindDef.defName == "JJK_TransfiguredSoul_Kind");
                        pattern.recoveryUntil = 0; pattern.Begin(unaware); pattern.Resolve();
                        var souls = boss.Map.mapPawns.AllPawnsSpawned.Where(t => t.kindDef.defName == "JJK_TransfiguredSoul_Kind").ToList();
                        cleanup.AddRange(souls);
                        check(unaware.Destroyed && souls.Count == soulsBefore + 1, "Mahito transfigures an unprotected stationary human");
                        check(souls.Last().InMentalState, "factionless Mahito's transfigured monster attacks colonists");
                    }
                }
                check(DefDatabase<MainButtonDef>.GetNamed("JJK_Headquarters").tabWindowClass == typeof(MainTabWindow_Jujutsu), "Jujutsu management tab is registered");
                check(p.GetGizmos().OfType<Command_Action>().Any(g => g.defaultLabel == "JJK_Dossier".Translate()), "sorcerer exposes a dossier gizmo");
                Pawn doomedOwner = Human("Lost owner QA"); cleanup.Add(doomedOwner);
                Pawn escapee = Curse(); cleanup.Add(escapee);
                var doomedRoster = progress.GetAbsorbedCurses(doomedOwner); doomedRoster.Capture(escapee);
                doomedOwner.Destroy();
                check(doomedRoster.Count == 0 && escapee.Spawned && escapee.Faction == null && escapee.InMentalState,
                    "destroying the owner releases stored curses immediately as hostile creatures");
            }
            catch (Exception ex) { fail.Add(ex.ToString()); }
            finally { foreach (Thing t in cleanup.Distinct()) if (!t.Destroyed) { t.holdingOwner?.Remove(t); t.Destroy(); } }
            Log.Message($"[JJK Expansion QA] RESULT {pass} passed, {fail.Count} failed. " + string.Join(" | ", fail));
        }

        [DebugAction("Rim-Kaisen", "Prepare six-feature persistence check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            Pawn pupil = Human("Expansion Persistence QA"), mentor = Human("Expansion Mentor QA");
            Learn(pupil, "JJK_Speech_DontMove"); Learn(mentor, "JJK_Speech_DontMove");
            var plan = GameComponent_Jujutsu.Instance.Plan(pupil);
            plan.tree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_CursedSpeech"); plan.mentor = mentor; plan.enabled = true;
            plan.hour = GenLocalDate.HourInteger(pupil); plan.lessonProgress = 37;
            GameComponent_JJK.Instance.AddTechniqueXp(mentor, plan.tree, 1000);
            var post = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_TrainingPost"));
            GenSpawn.Spawn(post, CellFinder.RandomClosewalkCellNear(pupil.Position, pupil.Map, 2), pupil.Map);
            Pawn curse = Curse(); curse.Name = new NameSingle("Saved curse QA");
            var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, curse, curse.RaceProps.body.corePart); injury.Severity = 2; curse.health.AddHediff(injury);
            var roster = GameComponent_JJK.Instance.GetAbsorbedCurses(pupil); roster.Capture(curse); roster.releaseId = curse.ThingID; roster.SelectSacrifice(curse, false);
            pupil.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence).Severity = 0.3f; HediffComp_SukunaVessel.For(pupil).Bargain();
            var school = GameComponent_Jujutsu.Schools.First(f => !f.HostileTo(Faction.OfPlayer));
            GameComponent_Jujutsu.Instance.Standing(school).nextOfferTick = 0;
            GameComponent_Jujutsu.Instance.Accept(school, pupil.Map, 1, ContractKind.Infestation);
            Pawn boss = Curse("JJK_Boss_Jogo_Kind"); mentor.Position = CellFinder.RandomClosewalkCellNear(boss.Position, boss.Map, 2);
            boss.TryGetComp<CompBossPattern>().Begin(mentor);
            Find.Selector.ClearSelection(); Find.Selector.Select(pupil);
            Report();
        }
        [DebugAction("Rim-Kaisen", "Report six-feature persistence check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report()
        {
            Pawn pupil = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.Name?.ToStringShort == "Expansion Persistence QA");
            if (pupil == null) { Log.Message("[JJK Expansion QA] MISSING persistence pupil"); return; }
            var roster = GameComponent_JJK.Instance.GetAbsorbedCurses(pupil); var plan = GameComponent_Jujutsu.Instance.Plan(pupil);
            var vessel = HediffComp_SukunaVessel.For(pupil);
            Log.Message($"[JJK Expansion QA] PERSIST pupil={pupil.ThingID}; mentor={plan.mentor?.ThingID}; lesson={plan.lessonProgress}; daily={plan.dailyXp}; mastery={GameComponent_JJK.Instance.GetTechniqueXp(pupil, plan.tree)}; curses={roster.Count}; selected={roster.SelectedCount}; release={roster.releaseId}; injury={roster.Pawns.FirstOrDefault()?.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity)}; loan={vessel?.loanUntil}; debt={vessel?.debt}; contracts={GameComponent_Jujutsu.Instance.contracts.Count}; telegraphs={Find.CurrentMap.mapPawns.AllPawnsSpawned.Count(p => p.TryGetComp<CompBossPattern>()?.Charging == true)}; job={pupil.CurJob?.def.defName}");
        }
        [DebugAction("Rim-Kaisen", "Start QA supervised practice", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartPractice()
        {
            Pawn pupil = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First(p => p.Name?.ToStringShort == "Expansion Persistence QA");
            var plan = GameComponent_Jujutsu.Instance.Plan(pupil);
            foreach (Pawn threat in Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.TryGetComp<CompBossPattern>() != null
                || p.def.HasModExtension<CursedSpiritExtension>()).ToList()) threat.Destroy();
            var post = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_TrainingPost")).First();
            plan.mentor.Position = CellFinder.RandomClosewalkCellNear(post.Position, pupil.Map, 2);
            pupil.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_Practice"), post, plan.mentor));
        }

        [DebugAction("Rim-Kaisen", "Finish six-feature persistence check", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Finish()
        {
            Pawn pupil = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First(p => p.Name?.ToStringShort == "Expansion Persistence QA");
            var roster = GameComponent_JJK.Instance.GetAbsorbedCurses(pupil);
            string expected = roster.releaseId;
            Pawn released = roster.Release(pupil.Position, pupil.Map);
            Log.Message($"[JJK Expansion QA] Loaded release identity={released?.ThingID == expected} injuries={released?.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity)}");
            if (released != null) roster.Capture(released);
            var vessel = HediffComp_SukunaVessel.For(pupil);
            vessel.loanUntil = Find.TickManager.TicksGame - 1;
            vessel.TickState(); vessel.EndPossession();
            bool temporaryRemoved = !pupil.genes.GenesListForReading.Any(g => g.def.defName == "JJK_CT_Gene_Shrine");
            Log.Message($"[JJK Expansion QA] Loaded temporary Shrine cleanup={temporaryRemoved}; possessed={vessel.Possessed}");
        }
    }
}
