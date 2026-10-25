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
    public static class JJK_DebugPuppetChecks
    {
        private static Pawn Human(Map map, IntVec3 cell, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            p.Name = new NameSingle(name);
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            GenSpawn.Spawn(p, cell, map);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true);
            p.GetComp<CompAbilities>().RecheckAbilities(); GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Special);
            PuppetUtility.Energy(p).energy = 500;
            return p;
        }
        private static IntVec3 Clear(Map map) => GenRadial.RadialCellsAround(map.Center, 60, true)
            .First(c => GenRadial.RadialCellsAround(c, 9, true).All(x => x.InBounds(map) && x.Standable(map)));
        private static Pawn Unit(Pawn owner, IntVec3 pos, bool mech)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(mech ? "JJK_Mechamaru_Kind" : "JJK_CursedCorpseDoll_Kind"), owner.Faction,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 0, fixedChronologicalAge: 0, fixedGender: Gender.None));
            var c = p.TryGetComp<CompCursedCorpse>(); c.controller = owner; c.charge = 60; c.autoFire = false; c.follow = false;
            GenSpawn.Spawn(p, pos, owner.Map); return p;
        }
        [DebugAction("Rim-Kaisen", "Run puppet regression checks", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int pass = 0; var failures = new List<string>(); var made = new List<Thing>();
            Action<bool, string> check = (ok, name) => { if (ok) pass++; else failures.Add(name); Log.Message("[JJK Puppet QA] " + (ok ? "PASS " : "FAIL ") + name); };
            Map map = Find.CurrentMap;
            try
            {
                IntVec3 cell = Clear(map);
                Pawn owner = Human(map, cell, "Puppet tester"); made.Add(owner);
                foreach (var gene in owner.genes.GenesListForReading.Where(g => g.def == PuppetUtility.Gene).ToList()) owner.genes.RemoveGene(gene);
                check(PuppetUtility.BindReason(owner, false) == null && PuppetUtility.BindReason(owner, true) != null, "ordinary sorcerer can bind dolls but not Mechamaru");
                Thing shell = GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_DollShell"), cell, map); made.Add(shell);
                float ce = PuppetUtility.Energy(owner).energy;
                shell.TryGetComp<CompUseEffect_ActivatePuppet>().DoEffect(owner);
                Pawn doll = GameComponent_Puppets.Instance.units.Last(p => p.TryGetComp<CompCursedCorpse>()?.controller == owner); made.Add(doll);
                var d = doll.TryGetComp<CompCursedCorpse>();
                check(shell.Destroyed && d.charge == 60 && PuppetUtility.Energy(owner).energy == ce - 30, "activation consumes one shell and exactly 30 CE");
                check(doll.gender == Gender.None && doll.ageTracker.AgeBiologicalTicks == 0, "crafted construct starts new and without a biological sex");
                check(PuppetUtility.BindReason(owner, false) != null, "ordinary controller bound-unit cap enforced");
                check(!JJK_Utils.IsCursedSpirit(doll) && !doll.RaceProps.IsMechanoid && !doll.RaceProps.IsFlesh, "construct classified separately from spirits and mechanoids");
                check(doll.needs.AllNeeds.Count == 0 && doll.health.hediffSet.BleedRateTotal == 0, "construct has no biological needs or bleeding");
                owner.genes.AddGene(PuppetUtility.Gene, true); owner.GetComp<CompAbilities>().RecheckAbilities();
                check(PuppetUtility.Limit(owner) == 3, "Special puppet manipulator can bind three constructs");
                check(JujutsuTraining.Class(owner).UnlockedTrees.Contains(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_PuppetManipulation")), "gene immediately unlocks Puppet Manipulation tree");
                Pawn mech = Unit(owner, cell + IntVec3.East * 2, true); made.Add(mech); var m = mech.TryGetComp<CompCursedCorpse>();
                check(m.Operational && d.Operational, "charged constructs operate");
                m.destination = cell + IntVec3.North * 3;
                var moveJob = new JobGiver_CursedCorpse().TryGiveJob(mech);
                check(moveJob.def == JobDefOf.Goto && moveJob.targetA.Cell == m.destination, "remote move order chooses the commanded cell"); m.destination = IntVec3.Invalid;
                Pawn enemy = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Grade4_Kind")); made.Add(enemy); GenSpawn.Spawn(enemy, cell + IntVec3.North * 5, map);
                float before = m.charge;
                check(m.TryMove(PuppetMove.UltraCannon, enemy) && m.charge == before - 6 && m.nextShot > Find.TickManager.TicksGame, "Ultra Cannon launches and consumes six charge");
                check(!m.TryMove(PuppetMove.UltraCannon, enemy) && m.charge == before - 6, "weapon cooldown blocks free extra shots");
                check(!m.TryMove(PuppetMove.UltraSpin, enemy), "Ultra Spin rejects distant targets");
                m.nextShot = 0;
                check(!m.TryMove(PuppetMove.UltraCannon, owner), "direct cannon order rejects allies");
                m.charge = 120;
                check(m.TryMove(PuppetMove.UltraShield) && mech.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_PuppetShield")), "Ultra Shield grants temporary protection");
                check(!m.TryMove(PuppetMove.BoostOn), "support moves share a cooldown");
                m.nextSupport = 0;
                check(m.TryMove(PuppetMove.BoostOn) && mech.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_PuppetBoost")), "Boost On grants movement enhancement");
                GameComponent_JJK.Instance.SetGrade(owner, SorcererGrade.Grade2);
                check(!m.TryMove(PuppetMove.UltimateCannon, enemy), "Ultimate Cannon enforces Grade 1");
                GameComponent_JJK.Instance.SetGrade(owner, SorcererGrade.Special); m.charge = 120;
                check(m.TryMove(PuppetMove.UltimateCannon, enemy) && m.charge == 60, "Ultimate Cannon spends sixty charge");
                check(!m.TryMove(PuppetMove.UltimateCannon, enemy), "Ultimate Cannon cannot bypass its cooldown");
                ce = PuppetUtility.Energy(owner).energy; m.charge = 60;
                check(m.Recharge(owner) && m.charge == 120 && PuppetUtility.Energy(owner).energy == ce - 30, "recharge transfers thirty CE to sixty charge");
                check(!m.Recharge(owner), "full core rejects wasteful recharge");
                m.charge = 0; PuppetUtility.Energy(owner).energy = 29;
                check(!m.Recharge(owner) && m.charge == 0, "insufficient CE cannot recharge"); PuppetUtility.Energy(owner).energy = 200;
                check(!m.Operational, "empty core shuts down"); m.charge = 60;
                m.standby = true; check(!m.Operational && new JobGiver_CursedCorpse().TryGiveJob(mech).def == JobDefOf.Wait, "standby suppresses combat jobs"); m.standby = false;
                var amplification = owner.health.AddHediff(JJK_DefOf.JJK_DomainAmplification);
                check(!m.Linked && d.Operational, "amplification interrupts remote control but not autonomous dolls"); owner.health.RemoveHediff(amplification);
                var burnout = owner.health.AddHediff(JJK_DefOf.JJK_DomainBurnout);
                check(!m.Linked, "burnout interrupts remote control"); owner.health.RemoveHediff(burnout);
                var anesthetic = owner.health.AddHediff(HediffDefOf.Anesthetic);
                check(!m.Linked && d.Operational, "unconscious controller interrupts only remote puppets"); owner.health.RemoveHediff(anesthetic);
                var legs = owner.health.hediffSet.GetNotMissingParts().Where(p => p.def == BodyPartDefOf.Leg).ToList();
                foreach (var leg in legs) owner.health.AddHediff(HediffDefOf.MissingBodyPart, leg);
                check(owner.Downed && m.Linked, "conscious immobile controller can operate Mechamaru");
                foreach (var leg in legs) owner.health.RestorePart(leg);
                foreach (var gene in owner.genes.GenesListForReading.Where(g => g.def == PuppetUtility.Gene).ToList()) owner.genes.RemoveGene(gene);
                check(!m.Linked, "loss of innate gene shuts remote link down"); owner.genes.AddGene(PuppetUtility.Gene, true);
                owner.DeSpawn(); check(!m.Linked && d.Operational, "off-map controller breaks remote link while doll keeps guarding"); GenSpawn.Spawn(owner, cell, map);
                var injury = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Crack"), mech, mech.RaceProps.body.corePart); injury.Severity = 35; mech.health.AddHediff(injury);
                check(!((Hediff_Injury)injury).CanHealNaturally(), "construct injuries cannot heal naturally");
                check(m.Repair() && Math.Abs(injury.Severity - 5) < 0.01, "repair pool removes exactly thirty injury damage");
                var arm = mech.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "MechanicalArm"); mech.health.AddHediff(HediffDefOf.MissingBodyPart, arm);
                check(m.Repair() && !mech.health.hediffSet.PartIsMissing(arm), "repair restores one missing part");
                m.charge = 40;
                var rechargeDef = DefDatabase<AbilityDef>.GetNamed("JJK_Puppet_RemoteRecharge");
                var ac = JujutsuTraining.Class(owner); ac.LearnAbility(rechargeDef, false);
                var recharge = ac.GetLearnedAbility(rechargeDef); recharge.curTarget = mech; ce = PuppetUtility.Energy(owner).energy;
                recharge.Start();
                check(m.charge == 100 && PuppetUtility.Energy(owner).energy == ce - 30, "TMF remote recharge targets own puppet and charges exactly once");
                recharge.curTarget = owner; ce = PuppetUtility.Energy(owner).energy; recharge.Start();
                check(PuppetUtility.Energy(owner).energy == ce, "invalid remote recharge target does not consume energy");
                var cannonDef = DefDatabase<AbilityDef>.GetNamed("JJK_Puppet_UltraCannon"); ac.LearnAbility(cannonDef, false);
                var cannon = (Ability_PuppetOrder)ac.GetLearnedAbility(cannonDef); m.nextShot = 0;
                check(cannon.Source(enemy) == m, "controller cannon selects an eligible owned puppet");
                enemy.Position = cell + IntVec3.North * 40;
                check(cannon.Source(enemy) == null, "remote caster range cannot bypass puppet weapon range");
                d.controller = null; check(d.Operational && !m.Unit.Dead, "unbound ordinary doll continues on stored energy");
                check(DefDatabase<RecipeDef>.AllDefsListForReading.Count(r => r.products?.Any(p => new[] { "JJK_DollShell", "JJK_MechamaruShell", "JJK_PuppetRepairKit" }.Contains(p.thingDef.defName)) == true) == 3, "all three construction recipes registered");
                Log.Message("[JJK Puppet QA] RESULT " + pass + " passed, " + failures.Count + " failed. " + string.Join("; ", failures));
            }
            catch (Exception ex) { Log.Error("[JJK Puppet QA] exception " + ex); }
            finally
            {
                foreach (var proj in map.listerThings.AllThings.OfType<Projectile>().Where(p => made.Contains(p.Launcher)).ToList()) proj.Destroy();
                foreach (Thing t in made.AsEnumerable().Reverse()) if (!t.Destroyed) t.Destroy();
                GameComponent_Puppets.Instance.units.RemoveAll(p => p == null || p.Destroyed || p.Dead);
            }
        }
        [DebugAction("Rim-Kaisen", "Prepare puppet persistence check", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            Map map = Find.CurrentMap; IntVec3 cell = Clear(map);
            var owner = Human(map, cell, "Puppet Persistence QA"); owner.genes.AddGene(PuppetUtility.Gene, true);
            var unit = Unit(owner, cell + IntVec3.East * 2, true); unit.Name = new NameSingle("Mechamaru Persistence QA");
            var c = unit.TryGetComp<CompCursedCorpse>(); c.charge = 47; c.standby = true; c.nextUltimate = Find.TickManager.TicksGame + 1500;
            var injury = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Crack"), unit, unit.RaceProps.body.corePart); injury.Severity = 8; unit.health.AddHediff(injury);
            GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_PuppetRepairKit"), cell + IntVec3.West, map);
            Report();
        }
        [DebugAction("Rim-Kaisen", "Report puppet persistence check", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report()
        {
            foreach (var p in GameComponent_Puppets.Instance.units.Where(p => p != null && !p.Destroyed && p.Name?.ToStringFull == "Mechamaru Persistence QA"))
            {
                var c = p.TryGetComp<CompCursedCorpse>();
                Log.Message($"[JJK Puppet QA] SAVED unit={p.ThingID} owner={c.controller?.ThingID} charge={c.charge} standby={c.standby} nextUltimate={c.nextUltimate} injury={p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h=>h.Severity)} job={p.CurJob?.def.defName}");
            }
        }
    }
}
