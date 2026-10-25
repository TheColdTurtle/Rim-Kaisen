using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_DebugSorceryDevelopment
    {
        private static Pawn Human(Map map, IntVec3 cell, string name, Faction faction = null)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction ?? Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle(name);
            foreach (string gene in new[] { "JJK_DeepReserves", "JJK_CT_Gene_Shrine", "JJK_CT_Gene_TenShadows" }) p.genes.AddGene(DefDatabase<GeneDef>.GetNamed(gene), true);
            p.GetComp<CompAbilities>().RecheckAbilities(); GenSpawn.Spawn(p, cell, map);
            GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Special);
            var ac = p.GetComp<CompAbilities>().GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            ac.UnlockTree(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine"));
            ac.UnlockTree(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_TenShadows"));
            ac.LearnAbility(DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Dismantle"), false);
            p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy = 500;
            return p;
        }
        [DebugAction("Rim-Kaisen", "Run sorcery development checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void Queue() => LongEventHandler.QueueLongEvent(Run, "GeneratingMapForNewEncounter", false, null);
        public static void Run()
        {
            int pass = 0, fail = 0;
            Action<bool, string> check = (ok, label) => { if (ok) pass++; else fail++; Log.Message("[JJK Sorcery QA] " + (ok ? "PASS " : "FAIL ") + label); };
            var made = new List<Thing>(); var map = Find.CurrentMap;
            var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
            try
            {
                var p = Human(map, cell, "Sorcery QA"); var ally = Human(map, cell + IntVec3.East, "Anchor QA");
                var enemy = Human(map, cell + IntVec3.East * 6, "Listener QA", Faction.OfAncientsHostile);
                made.AddRange(new[] { p, ally, enemy });
                var r = SorceryDevelopment.State(p);
                foreach (var hd in DefDatabase<HediffDef>.AllDefsListForReading.Where(h => h.defName.StartsWith("JJK_Inherit")))
                    check(hd.stages == null || hd.stages.All(s => (s.statFactors == null || s.statFactors.All(f => f.stat != null)) && (s.statOffsets == null || s.statOffsets.All(f => f.stat != null))), hd.defName + " resolves all stat references");
                BindingVowUtil.ApplyVow(p, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Protector"), ally);
                check(r.protectedPawn == ally && r.ProtectionActive, "protector binds the selected pawn and activates nearby");
                check(Mathf.Approximately(SorceryDevelopment.OutputFactor(p), 1.3f), "protector grants 30% output");
                BindingVowUtil.ApplyVow(p, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Spoken"));
                check(!SorceryDevelopment.Has(p, "JJK_Vow_Spoken"), "one-vow limit rejects second vow");
                ally.Position = cell + IntVec3.East * 14;
                check(!r.ProtectionActive && Mathf.Approximately(SorceryDevelopment.OutputFactor(p), 1), "distance suspends protection bonus");
                ally.Position = cell + IntVec3.East;
                var shield = ally.health.AddHediff(JJK_DefOf.JJK_Infinity);
                var blocked = ally.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5, instigator: p));
                check(blocked.totalDamageDealt == 0 && !r.protectionBroken, "fully blocked friendly hit preserves protection vow");
                ally.health.RemoveHediff(shield);
                ally.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 1, instigator: p));
                check(r.protectionBroken && Mathf.Approximately(SorceryDevelopment.OutputFactor(p), .75f), "harming protected pawn permanently breaks vow");
                p.health.RemoveHediff(p.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_Vow_Protector")));
                check(!SorceryDevelopment.Reveal(p, ally), "reveal rejects ally");
                check(SorceryDevelopment.Reveal(p, enemy), "reveal accepts nearby hearing enemy");
                check(!SorceryDevelopment.Reveal(p, enemy), "reveal cooldown prevents refresh");
                check(Mathf.Approximately(SorceryDevelopment.OutputFactor(p), 1.2f), "reveal grants temporary output");
                check(Mathf.Approximately(SorceryDevelopment.CounterFactor(p, enemy), .7f), "informed enemy resists caster damage");
                check(Mathf.Approximately(SorceryDevelopment.CounterFactor(enemy, p), 1.25f), "informed enemy exploits caster opening");
                check(Mathf.Approximately(SorceryDevelopment.CounterFactor(ally, enemy), 1f), "counterplay does not protect enemy against unrelated allies");
                r.revealUntil = SorceryDevelopment.Now;
                check(Mathf.Approximately(SorceryDevelopment.OutputFactor(p), 1f), "reveal expires");
                var ac = p.GetComp<CompAbilities>().GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
                var ability = ac.GetLearnedAbility(DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Dismantle"));
                check(ability != null, "fixture learns projectile technique");
                int castTicks = ability.CastTicks;
                BindingVowUtil.ApplyVow(p, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Spoken"));
                check(ability.CastTicks == castTicks + 180 && !ability.IsInstantAction, "spoken preparation adds three seconds and disables instant casting");
                var tongue = p.health.hediffSet.GetNotMissingParts().First(b => b.def.defName == "Tongue");
                var missing = p.health.AddHediff(HediffDefOf.MissingBodyPart, tongue);
                check(!SorceryDevelopment.CanSpeak(p) && !SorceryDevelopment.Allowed(ability, enemy), "lost voice blocks technique");
                string reason;
                check(!ability.CanBeActivated(0, out reason, false, null), "TMF activation refuses mute sworn caster");
                p.health.RemoveHediff(missing); p.health.RemoveHediff(p.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_Vow_Spoken")));
                BindingVowUtil.ApplyVow(p, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_CloseRange"));
                float range = SorceryDevelopment.Range(ability);
                check(range > 0, "targeted projectile range is derived from authored tier");
                check(SorceryDevelopment.Allowed(ability, new LocalTargetInfo(cell + IntVec3.East * (int)range)), "close-range boundary accepts inside target");
                check(!SorceryDevelopment.Allowed(ability, new LocalTargetInfo(cell + IntVec3.East * ((int)range + 2))), "close-range boundary rejects distant target");
                check(SorceryDevelopment.Allowed(ability, p), "self targeting remains usable");
                p.health.RemoveHediff(p.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_Vow_CloseRange")));
                var tree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine");
                check(!r.Refine(tree, DomainRefinement.Condensed), "refinement requires earned mastery");
                GameComponent_JJK.Instance.AddTechniqueXp(p, tree, 1000);
                check(r.Refine(tree, DomainRefinement.Condensed), "earned mastery unlocks refinement");
                check(!r.Refine(tree, DomainRefinement.Economical), "competing refinement is excluded");
                var domainDef = DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Domain_Shrine");
                var mgr = map.GetComponent<MapComponent_DomainManager>(); mgr.Open(p, domainDef);
                var domain = mgr.DomainFor(p);
                check(domain != null && domain.refinement == DomainRefinement.Condensed, "domain captures personal refinement at opening");
                check(Mathf.Approximately(domain.Radius, domain.Props.radius * .65f), "condensed domain shrinks actual radius");
                check(domain.Interior(map).All(c => c.DistanceToSquared(cell) <= domain.Radius * domain.Radius), "domain geometry respects reduced radius");
                check(Mathf.Approximately(domain.RefinementPower, 1.35f), "condensed sure-hit power increases");
                mgr.CollapseFor(p, true);
                var cheap = new ActiveDomain { refinement = DomainRefinement.Economical };
                check(cheap.RefinementPower == .85f && cheap.RefinementUpkeep == .65f, "economical domain trades strength for upkeep");
                var fortified = new ActiveDomain { refinement = DomainRefinement.Fortified };
                check(fortified.RefinementUpkeep == 1.2f, "fortification increases upkeep");
                var tenTree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_TenShadows");
                GameComponent_JJK.Instance.AddTechniqueXp(ally, tenTree, 1000);
                check(SorceryDevelopment.State(ally).Refine(tenTree, DomainRefinement.Fortified), "closed domain can choose fortification");
                mgr.Open(ally, DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Domain_TenShadows"));
                ally.SetFaction(Faction.OfAncientsHostile);
                mgr.Open(p, domainDef);
                check(mgr.HasDomain(ally) && !mgr.HasDomain(p), "fortified barrier wins clash against equal earned mastery");
                mgr.CollapseFor(ally, true);
                ally.SetFaction(Faction.OfPlayer);
                var toolDef = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.defName.StartsWith("JJK_") && d.GetCompProperties<CompProperties_CursedTool>() != null && !d.MadeFromStuff);
                var weapon = (ThingWithComps)ThingMaker.MakeThing(toolDef); made.Add(weapon);
                p.equipment.DestroyAllEquipment(); p.equipment.AddEquipment(weapon);
                var tool = weapon.TryGetComp<Comp_CursedTool>(); tool.LandedHit(p, enemy);
                check(tool.attunedPawn == p && !tool.Attuned, "first hostile hit begins exclusive weapon bond");
                tool.Wield(ally, 180000); check(tool.wieldTicks == 0, "another wielder cannot progress the bond");
                tool.Wield(p, 180000); check(!tool.Attuned, "time alone does not unlock signature");
                tool.bondedHits = 20; check(tool.Attuned, "time and practice together unlock signature");
                tool.LandedHit(p, enemy); check(tool.nextSignature == SorceryDevelopment.Now + 600, "signature starts limited cooldown");
                int cooldown = tool.nextSignature; tool.LandedHit(p, enemy); check(tool.nextSignature == cooldown, "rapid hits do not refresh signature");
                tool.Notify_Unequipped(p); check(tool.lossReported && p.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "JJK_AttunedToolLost"), "weapon loss causes grief");
                tool.Wield(p, 60); check(!tool.lossReported && !p.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "JJK_AttunedToolLost"), "recovering weapon clears loss memory");
                var shadows = GameComponent_JJK.Instance.GetTenShadows(p);
                var ten = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_TenShadows");
                shadows.SetState(ShadowInheritance.Shadow("Toad"), ShikigamiState.Lost);
                shadows.SetState(ShadowInheritance.Shadow("Nue"), ShikigamiState.Available);
                check(!ShadowInheritance.CanInherit(p, 0), "inheritance requires mastery");
                GameComponent_JJK.Instance.AddTechniqueXp(p, ten, 500);
                check(ShadowInheritance.Inherit(p, 0), "lost compatible donor can empower tamed heir");
                check(!ShadowInheritance.Inherit(p, 0), "same inheritance cannot be reused");
                shadows.SetState(ShadowInheritance.Shadow("GreatSerpent"), ShikigamiState.Lost);
                check(!ShadowInheritance.Inherit(p, 1), "heir cannot stack a second donor");
                var summon = JJK_SummonUtility.SummonShadow(p, ShadowInheritance.Shadow("Nue").pawnKind, 6000, false, ShadowInheritance.Shadow("Nue")); made.Add(summon);
                check(SorceryDevelopment.Has(summon, "JJK_InheritGrip"), "inherited effect reaches actual summoned heir");
                check(!shadows.CanSummon(ShadowInheritance.Shadow("Toad")), "inheritance never restores lost donor");
                var traces = map.GetComponent<MapComponent_ResidualEnergy>(); traces.Record(enemy, tree);
                check(traces.Nearby(p).Any(t => t.source == enemy), "scanner detects hostile cast residue");
                traces.Record(p, tree); check(!traces.Nearby(p).Any(t => t.source == p), "scanner excludes friendly residue");
                var trace = traces.traces.Last(t => t.source == enemy); trace.tick = SorceryDevelopment.Now - 15001;
                check(!traces.Nearby(p).Any(t => t.id == trace.id), "old residue expires from detection");
                var previousJob = p.CurJob;
                traces.Follow(p, trace);
                check(p.CurJob == previousJob, "stale scan menu cannot order an expired trail");
                trace.tick = SorceryDevelopment.Now - 60; trace.castTick = SorceryDevelopment.Now - 2400; trace.followed = true;
                int samples = traces.traces.Count;
                traces.Record(enemy, ten);
                check(traces.traces.Count == samples && trace.castTick == SorceryDevelopment.Now && trace.tree == ten && !trace.followed,
                    "rapid recast renews technique and movement window without duplicate sample");
                int originalCastTick = Math.Max(0, SorceryDevelopment.Now - 2400);
                trace.castTick = originalCastTick;
                traces.Record(enemy, tree, trace.castTick);
                check(trace.castTick == originalCastTick && trace.tree == ten, "movement sample cannot renew cast lifetime or replace technique");
                var influence = p.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence); influence.Severity = .1f;
                var vessel = HediffComp_SukunaVessel.For(p);
                check(VesselDevelopment.QuietBargain(p), "silence bargain applies one-time permanent cost");
                check(SorceryDevelopment.Has(p, "JJK_VesselQuietPrice") && vessel.suppressedUntil == SorceryDevelopment.Now + 180000, "silence suppresses seizures for three days");
                check(!VesselDevelopment.QuietBargain(p), "silence price cannot be stacked");
                int purchasedSilence = vessel.suppressedUntil;
                // Select a winning random stream, then execute the actual confrontation.
                int winningSeed = 0;
                for (; winningSeed < 1000; winningSeed++)
                {
                    Rand.PushState(winningSeed);
                    bool wins = Rand.Chance(.55f - influence.Severity * .25f);
                    Rand.PopState();
                    if (wins) break;
                }
                Rand.PushState(winningSeed);
                try { check(VesselDevelopment.FinishConfrontation(p) && r.resolve == 1, "internal confrontation completes with earned resolve"); }
                finally { Rand.PopState(); }
                check(vessel.suppressedUntil == purchasedSilence, "confrontation victory preserves longer purchased silence");
                vessel.suppressedUntil = 0;
                check(vessel.BeginPossession(), "possession opens expanded vessel crisis");
                check(r.rescueDeadline > SorceryDevelopment.Now, "possession creates timed rescue objective");
                check(!VesselDevelopment.CanRescue(ally, p), "active berserk vessel must be subdued first");
                vessel.EndPossession(); ally.Position = p.Position + IntVec3.East;
                check(VesselDevelopment.Rescue(ally, p), "nearby sorcerer anchors recovered vessel");
                check(r.rescueDeadline == 0 && r.rescues == 1, "rescue objective resolves exactly once");
                check(!VesselDevelopment.Rescue(ally, p), "resolved rescue cannot be farmed");
                check(p.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "JJK_VesselRescued" && m.otherPawn == ally), "rescue remembers specific rescuer");
                r.rescueDeadline = SorceryDevelopment.Now - 1; float before = influence.Severity; VesselDevelopment.Tick(r);
                check(r.rescueDeadline == 0 && Mathf.Approximately(influence.Severity, before + .1f), "missed rescue increases influence once");
            }
            catch (Exception e) { fail++; Log.Error("[JJK Sorcery QA] " + e); }
            finally
            {
                foreach (var thing in made) if (!thing.Destroyed) thing.Destroy();
                GameComponent_SorceryDevelopment.Instance.records.RemoveAll(r => r.pawn.Destroyed);
            }
            Log.Message("[JJK Sorcery QA] RESULT " + pass + " passed, " + fail + " failed.");
        }
        [DebugAction("Rim-Kaisen", "Prepare sorcery persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PreparePersistence()
        {
            var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var p = Human(map, cell, "Sorcery Persistence"); var ally = Human(map, cell + IntVec3.East, "Sorcery Protected");
            var r = SorceryDevelopment.State(p); r.protectedPawn = ally; r.revealedTo = ally; r.revealUntil = SorceryDevelopment.Now + 1800;
            r.rescueDeadline = SorceryDevelopment.Now + 6000; r.resolve = 3; r.rescues = 2;
            var tree = DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine"); GameComponent_JJK.Instance.AddTechniqueXp(p, tree, 1000); r.Refine(tree, DomainRefinement.Condensed);
            r.donors.Add(ShadowInheritance.Shadow("Toad")); r.heirs.Add(ShadowInheritance.Shadow("Nue"));
            BindingVowUtil.ApplyVow(p, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Protector"), ally);
            p.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence).Severity = .1f;
            var def = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.defName.StartsWith("JJK_") && d.GetCompProperties<CompProperties_CursedTool>() != null && !d.MadeFromStuff);
            p.equipment.DestroyAllEquipment(); var weapon = (ThingWithComps)ThingMaker.MakeThing(def); p.equipment.AddEquipment(weapon);
            var tool = weapon.TryGetComp<Comp_CursedTool>(); tool.attunedPawn = p; tool.wieldTicks = 180000; tool.bondedHits = 20; tool.nextSignature = SorceryDevelopment.Now + 600;
            map.GetComponent<MapComponent_ResidualEnergy>().Record(p, tree);
            map.GetComponent<MapComponent_DomainManager>().Open(p, DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Domain_Shrine"));
            Find.Selector.ClearSelection(); Find.Selector.Select(p); Find.WindowStack.Add(new Window_SorceryDevelopment(p));
            ReportPersistence();
        }
        [DebugAction("Rim-Kaisen", "Report sorcery persistence", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReportPersistence()
        {
            var p = Find.CurrentMap.mapPawns.AllPawnsSpawned.First(x => x.Name?.ToStringShort == "Sorcery Persistence");
            var r = SorceryDevelopment.State(p); var tool = p.equipment.Primary.TryGetComp<Comp_CursedTool>();
            var domain = p.Map.GetComponent<MapComponent_DomainManager>().DomainFor(p);
            var checks = new[] { r.protectedPawn?.Name?.ToStringShort == "Sorcery Protected", r.revealedTo == r.protectedPawn,
                r.resolve == 3, r.rescues == 2, r.rescueDeadline > SorceryDevelopment.Now, r.revealUntil > SorceryDevelopment.Now,
                r.Refinement(DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine")) == DomainRefinement.Condensed,
                r.donors.Contains(ShadowInheritance.Shadow("Toad")), r.heirs.Contains(ShadowInheritance.Shadow("Nue")),
                tool.Attuned, tool.attunedPawn == p, tool.nextSignature > SorceryDevelopment.Now,
                p.Map.GetComponent<MapComponent_ResidualEnergy>().traces.Any(t => t.source == p), domain?.refinement == DomainRefinement.Condensed };
            Log.Message("[JJK Sorcery Persistence] " + checks.Count(x => x) + "/" + checks.Length + " " + string.Join(",", checks));
        }
        [DebugAction("Rim-Kaisen", "Prepare sorcery live jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PrepareJobs()
        {
            var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var vessel = Human(map, cell, "Sorcery Job Vessel");
            var rescuer = Human(map, cell + IntVec3.East, "Sorcery Job Rescuer");
            var thinker = Human(map, cell + IntVec3.North * 4, "Sorcery Job Thinker");
            var sworn = Human(map, cell + IntVec3.West * 4, "Sorcery Job Sworn");
            var tracker = Human(map, cell + IntVec3.South * 5, "Sorcery Job Tracker");
            var speaker = Human(map, cell + IntVec3.North * 8, "Sorcery Job Speaker");
            BindingVowUtil.ApplyVow(speaker, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Spoken"));
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.BlocksGranite);
            GenSpawn.Spawn(wall, speaker.Position + IntVec3.East * 6, map);
            speaker.drafter.Drafted = true;
            speaker.GetComp<CompAbilities>().GetAbilityClass(JJK_DefOf.JJK_CursedTechniques)
                .GetLearnedAbility(DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Dismantle")).DoCast(wall);
            vessel.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence).Severity = .1f;
            SorceryDevelopment.State(vessel).rescueDeadline = SorceryDevelopment.Now + 6000;
            thinker.health.AddHediff(JJK_DefOf.JJK_SukunaInfluence).Severity = .1f;
            rescuer.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_AnchorVessel"), vessel));
            VesselDevelopment.Confront(thinker);
            var altarDef = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.GetCompProperties<CompProperties_BindingVowStation>() != null);
            var altar = ThingMaker.MakeThing(altarDef, altarDef.MadeFromStuff ? ThingDefOf.BlocksGranite : null);
            altar.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(altar, cell + IntVec3.West * 6, map);
            altar.TryGetComp<CompBindingVowStation>().OrderVow(sworn, DefDatabase<BindingVowDef>.GetNamed("JJK_BindingVow_Protector"), vessel);
            var source = Human(map, tracker.Position + IntVec3.East * 4, "Sorcery Job Trail Source", Faction.OfAncientsHostile);
            var trails = map.GetComponent<MapComponent_ResidualEnergy>(); trails.nextCamp = 0;
            trails.Record(source, DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine"));
            var first = trails.traces.Last();
            source.Position += IntVec3.East * 4; trails.Record(source, DefDatabase<AbilityTreeDef>.GetNamed("JJK_CT_Shrine"));
            source.DeSpawn(); Find.WorldPawns.PassToWorld(source);
            trails.Follow(tracker, first);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Log.Message("[JJK Sorcery Live] Queued anchor, inner confrontation, named protection ritual and two-node trail to a camp.");
        }
        [DebugAction("Rim-Kaisen", "Report sorcery live jobs", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ReportJobs()
        {
            var map = Find.CurrentMap;
            foreach (var p in map.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringShort.StartsWith("Sorcery Job") == true))
            {
                var r = SorceryDevelopment.State(p);
                Log.Message("[JJK Sorcery Live] " + p.Name.ToStringShort + " job=" + p.CurJobDef?.defName + " CE=" + p.GetComp<CompAbilities>().GetAbilityResource(JJK_DefOf.JJK_CursedEnergy).energy
                    + " rescue=" + r.rescues + " deadline=" + r.rescueDeadline + " confrontation=" + r.nextConfrontation
                    + " vow=" + SorceryDevelopment.Has(p, "JJK_Vow_Protector") + " protected=" + r.protectedPawn?.Name?.ToStringShort);
                if (p.Name.ToStringShort == "Sorcery Job Speaker") Log.Message("[JJK Sorcery Live] spoken casts=" + map.GetComponent<MapComponent_ResidualEnergy>().traces.Count(t => t.source == p));
            }
            var trails = map.GetComponent<MapComponent_ResidualEnergy>();
            Log.Message("[JJK Sorcery Live] followed=" + trails.traces.Count(t => t.source?.Name?.ToStringShort == "Sorcery Job Trail Source" && t.followed)
                + " camp=" + Find.WorldObjects.Sites.Count(s => s.parts.Any(p => p.def.defName == "JJK_ResidualCamp")));
        }
        [DebugAction("Rim-Kaisen", "Visit residual camp QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void VisitCamp() => LongEventHandler.QueueLongEvent(() =>
        {
            var site = Find.WorldObjects.Sites.First(s => s.parts.Any(p => p.def.defName == "JJK_ResidualCamp"));
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, new IntVec3(100, 1, 100), null);
            Current.Game.CurrentMap = map;
            Log.Message("[JJK Residual Camp QA] hostile defenders=" + map.mapPawns.AllPawnsSpawned.Count(p => p.HostileTo(Faction.OfPlayer))
                + " site=" + site.Label + " generated=" + site.HasMap);
        }, "GeneratingMapForNewEncounter", false, null);
    }
}
