using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_DebugAdvancedPuppetChecks
    {
        internal static bool TryFindClear(Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null) return false;
            foreach (var candidate in GenRadial.RadialCellsAround(map.Center, 75, true))
            {
                bool clear = true;
                foreach (var offset in GenRadial.RadialCellsAround(candidate, 10, true))
                    if (!offset.InBounds(map) || !offset.Standable(map)) { clear = false; break; }
                if (clear) { cell = candidate; return true; }
            }
            return false;
        }
        internal static IntVec3 Clear(Map map)
        {
            if (TryFindClear(map, out var cell)) return cell;
            throw new System.InvalidOperationException("QA setup needs a clear radius-10 area within 75 cells of the map center. Use the prepared QA arena.");
        }
        private static Pawn Human(Map map, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.genes.AddGene(DefDatabase<GeneDef>.GetNamed("JJK_DeepReserves"), true); p.genes.AddGene(PuppetUtility.Gene, true);
            p.GetComp<CompAbilities>().RecheckAbilities(); GenSpawn.Spawn(p, cell, map); GameComponent_JJK.Instance.SetGrade(p, SorcererGrade.Special); PuppetUtility.Energy(p).energy = 500; return p;
        }
        private static Pawn Unit(Pawn owner, string kind, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind), owner.Faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 0, fixedChronologicalAge: 0, fixedGender: Gender.None));
            var c = p.TryGetComp<CompCursedCorpse>(); c.controller = owner; c.charge = 120; c.follow = false; c.autoFire = false;
            GenSpawn.Spawn(p, cell, owner.Map); return p;
        }
        [DebugAction("Rim-Kaisen", "Run advanced puppet checks", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            int passed = 0; var failed = new List<string>(); var made = new List<Thing>(); Map map = Find.CurrentMap;
            Action<bool,string> check = (ok,name) => { if (ok) passed++; else failed.Add(name); Log.Message("[JJK Advanced Puppet QA] " + (ok ? "PASS " : "FAIL ") + name); };
            try
            {
                IntVec3 cell = Clear(map); var owner = Human(map, cell); made.Add(owner);
                var panda = Unit(owner, "JJK_PandaConstruct_Kind", cell + IntVec3.East * 2); made.Add(panda);
                var triple = panda.TryGetComp<CompThreeCores>(); var core = triple.Core;
                check(!triple.Mature && Enum.GetValues(typeof(PandaForm)).Cast<PandaForm>().All(triple.Available), "three distinct intact cores start with a saved maturation deadline");
                int slots = GameComponent_Puppets.Instance.Count(owner); triple.maturesAt = 0;
                check(triple.Mature && GameComponent_Puppets.Instance.Count(owner) == slots - 1, "mature companion no longer consumes a control slot");
                float before = core.charge;
                check(triple.Switch(PandaForm.Gorilla) && core.charge == before - 5 && panda.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_PandaForm_Gorilla")), "Gorilla form changes stats and consumes five charge");
                check(!triple.Switch(PandaForm.Sister), "form switch cooldown enforced"); triple.nextSwitch = 0;
                check(triple.Switch(PandaForm.Sister), "sister form can be selected");
                var injury = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Crack"), panda, panda.RaceProps.body.corePart); injury.Severity = 5; panda.health.AddHediff(injury);
                triple.nextSwitch = 0; triple.Switch(PandaForm.Panda);
                check(injury.Severity == 5, "changing forms preserves existing injuries");
                panda.health.RemoveHediff(injury);
                panda.health.AddHediff(HediffDefOf.MissingBodyPart, triple.Part(PandaForm.Gorilla)); triple.UpdateCores();
                check(!panda.Dead && !triple.Available(PandaForm.Gorilla), "destroying one core disables only its form");
                check(!core.Repair() && panda.health.hediffSet.PartIsMissing(triple.Part(PandaForm.Gorilla)), "ordinary repair kits cannot replace destroyed soul cores");
                core.controller = null; check(core.Operational, "independent companion functions without creator"); core.controller = owner;

                var frame = Unit(owner, "JJK_AbsolutePuppet_Kind", cell + IntVec3.West * 2); made.Add(frame); var absolute = frame.TryGetComp<CompAbsolutePuppet>();
                check(!absolute.Core.Operational, "unoccupied giant frame is inactive");
                before = PuppetUtility.Energy(owner).energy;
                check(absolute.Deposit(owner) && absolute.reservoir == 50 && PuppetUtility.Energy(owner).energy == before - 50, "reservoir deposits transfer energy one-for-one");
                absolute.reservoir = 2390; before = PuppetUtility.Energy(owner).energy;
                check(!absolute.Deposit(owner) && PuppetUtility.Energy(owner).energy == before, "reservoir overflow rejected without energy loss"); absolute.reservoir = 1000;
                string id = owner.ThingID;
                check(absolute.Board(owner) && absolute.Pilot == owner && !owner.Spawned && absolute.Core.Operational, "real pilot enters cockpit and enables the giant frame");
                var children = new List<IThingHolder>(); frame.GetChildHolders(children);
                check(children.Contains(absolute) && absolute.GetDirectlyHeldThings().Contains(owner), "cockpit exposes pilot to the engine holder tree");
                check(!absolute.Board(owner), "occupied cockpit rejects duplicate boarding");
                for (int i=0;i<4;i++) absolute.LoadCartridge();
                check(absolute.cartridges == 4 && !absolute.LoadCartridge(), "cartridge loader respects four-tube limit");
                check(absolute.Fire(AbsoluteMove.CockpitDomain) && absolute.cartridges == 3 && absolute.Shielded, "cockpit domain consumes a cartridge");
                bool ward = (bool)AccessTools.Method(typeof(MapComponent_DomainManager), "IsShieldedFromSureHit").Invoke(map.GetComponent<MapComponent_DomainManager>(), new object[] { frame });
                check(ward, "cockpit ward is connected to actual domain sure-hit protection");
                absolute.nextAttack = 0;
                var enemy = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Carapace_Kind")); made.Add(enemy); GenSpawn.Spawn(enemy, cell + IntVec3.North * 6, map);
                before = absolute.reservoir;
                check(absolute.Fire(AbsoluteMove.OneYear, enemy) && absolute.reservoir == before - 120, "one-year cannon spends 120 stored energy");
                check(!absolute.Fire(AbsoluteMove.Miracle, enemy), "giant weapons enforce shared recovery"); absolute.nextAttack = 0;
                before = absolute.reservoir; check(absolute.Fire(AbsoluteMove.Miracle, enemy) && absolute.reservoir == before - 240, "Miracle Cannon spends 240 stored energy"); absolute.nextAttack = 0; absolute.reservoir = 600;
                check(absolute.Fire(AbsoluteMove.Pigeon, enemy) && absolute.reservoir == 0 && map.listerThings.AllThings.OfType<Projectile_PigeonViola>().Count(p=>p.Launcher == frame) == 5, "Pigeon Viola spends 600 and launches five tracking projectiles");
                absolute.nextAttack = 0; check(!absolute.Fire(AbsoluteMove.OneYear, enemy), "empty reservoir blocks giant attacks");
                check(absolute.Eject() && owner.Spawned && owner.ThingID == id && absolute.Pilot == null && !absolute.Shielded, "normal exit restores same pilot and ends cockpit protection");
                owner.Position = cell; check(absolute.Board(owner), "same controller can re-enter cockpit"); frame.Kill(null);
                check(owner.Spawned && !owner.Dead && absolute.Pilot == null, "destroyed frame safely ejects the real pilot");

                var comm = Unit(owner, "JJK_CommunicationPuppet_Kind", cell + IntVec3.South * 2); made.Add(comm);
                var relay = comm.TryGetComp<CompCommunicationPuppet>();
                int count = GameComponent_Puppets.Instance.Count(owner);
                check(relay.Contacts().Contains(enemy), "communication relay detects visible hostile contact");
                relay.Core.controller = null;
                check(relay.Core.Operational && GameComponent_Puppets.Instance.Count(owner) == count, "communication puppet operates without creator and occupies no combat slot");
                relay.Core.charge = 0; check(relay.Contacts().Count == 0, "exhausted communication core cannot scan");

                var faction = Find.FactionManager.AllFactions.First(f => f.def.defName == "JJK_CurseUsers");
                var npc = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_CursePuppetOperator"), faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false)); made.Add(npc); GenSpawn.Spawn(npc, cell + IntVec3.North * 8, map);
                var squad = GameComponent_Puppets.Instance.units.Where(p=>p.TryGetComp<CompCursedCorpse>()?.controller == npc).ToList(); made.AddRange(squad);
                check(squad.Count == 2 && squad.All(p=>p.Faction == faction && p.TryGetComp<CompCursedCorpse>().Operational), "authored NPC operator spawns two faction-aligned operational puppets");
                check(!Patch_NPCPuppetSquad.Provision(npc) && GameComponent_Puppets.Instance.Count(npc) == 2, "NPC squad provisioning is one-shot");
                npc.DeSpawn(); check(squad.All(p=>p.Destroyed), "retreating NPC takes remaining puppets off the map");

                panda.health.AddHediff(HediffDefOf.MissingBodyPart, triple.Part(PandaForm.Sister)); panda.health.AddHediff(HediffDefOf.MissingBodyPart, triple.Part(PandaForm.Panda)); triple.UpdateCores();
                check(panda.Dead, "loss of all three cores kills the companion");
                Log.Message("[JJK Advanced Puppet QA] RESULT " + passed + " passed, " + failed.Count + " failed. " + string.Join("; ",failed));
            }
            catch(Exception ex) { Log.Error("[JJK Advanced Puppet QA] exception " + ex); }
            finally
            {
                foreach(var projectile in map.listerThings.AllThings.OfType<Projectile>().Where(p=>made.Contains(p.Launcher)).ToList()) projectile.Destroy();
                foreach(var t in made.AsEnumerable().Reverse())
                {
                    if(t is Pawn p) { p.TryGetComp<CompAbsolutePuppet>()?.Eject(map); if(p.Corpse != null && !p.Corpse.Destroyed) p.Corpse.Destroy(); }
                    if(!t.Destroyed)t.Destroy();
                }
                GameComponent_Puppets.Instance.units.RemoveAll(p=>p==null || p.Destroyed || p.Dead);
            }
        }
        [DebugAction("Rim-Kaisen", "Prepare advanced puppet save", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            Map map=Find.CurrentMap; var cell=Clear(map); var owner=Human(map,cell); owner.Name=new NameSingle("Advanced Pilot QA");
            var panda=Unit(owner,"JJK_PandaConstruct_Kind",cell+IntVec3.East*3); panda.Name=new NameSingle("Three Cores QA"); var triple=panda.TryGetComp<CompThreeCores>();triple.maturesAt=0;triple.Switch(PandaForm.Gorilla);triple.Core.charge=50;triple.Core.standby=true;
            var frame=Unit(owner,"JJK_AbsolutePuppet_Kind",cell+IntVec3.West*2);frame.Name=new NameSingle("Absolute QA");var c=frame.TryGetComp<CompAbsolutePuppet>();c.reservoir=720;c.cartridges=3;c.Board(owner);c.Core.standby=true;
            // The pilot is now held, so use the spawned frame's map for a separate relay owner.
            var messenger=Human(map,cell+IntVec3.South*3);messenger.Name=new NameSingle("Relay Creator QA");
            var relay=Unit(messenger,"JJK_CommunicationPuppet_Kind",cell+IntVec3.South*4);relay.Name=new NameSingle("Relay QA");relay.TryGetComp<CompCommunicationPuppet>().message="Saved final message QA";relay.TryGetComp<CompCursedCorpse>().charge=37;
            Report();
        }
        [DebugAction("Rim-Kaisen", "Prepare advanced puppet combat", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Combat()
        {
            var map = Find.CurrentMap;
            var frame = GameComponent_Puppets.Instance.units.First(p => p.Name?.ToStringFull == "Absolute QA");
            var a = frame.TryGetComp<CompAbsolutePuppet>(); a.Core.standby = false; a.reservoir = 1200; a.nextAttack = 0;
            var cell = GenRadial.RadialCellsAround(frame.Position, 18, true).Last(c => c.InBounds(map) && c.Standable(map) && GenSight.LineOfSight(frame.Position, c, map));
            var enemy = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("JJK_Curse_Carapace_Kind"), Faction.OfAncientsHostile);
            enemy.Name = new NameSingle("Tracking Target QA"); GenSpawn.Spawn(enemy, cell, map);
            enemy.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(cell, map, 8)), JobCondition.InterruptForced);
            var creator = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.Name?.ToStringFull == "Relay Creator QA"); creator?.Kill(null);
            if (a.Pilot?.needs?.food != null) a.Pilot.needs.food.CurLevel = 0.8f;
            Log.Message($"[JJK Advanced Puppet QA] LIVE target={enemy.ThingID} at {cell}; frame={frame.Position}; pilot food={a.Pilot?.needs?.food?.CurLevel}");
        }
        [DebugAction("Rim-Kaisen", "Report advanced puppet save", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report()
        {
            foreach(var p in GameComponent_Puppets.Instance.units.Where(p=>p!=null&&!p.Destroyed&&p.Name?.ToStringFull?.EndsWith(" QA")==true))
            {
                var a=p.TryGetComp<CompAbsolutePuppet>();var t=p.TryGetComp<CompThreeCores>();var r=p.TryGetComp<CompCommunicationPuppet>();
                Log.Message($"[JJK Advanced Puppet QA] SAVED {p.Name}: id={p.ThingID} pos={p.Position} pilot={a?.Pilot?.ThingID} reservoir={a?.reservoir} tubes={a?.cartridges} form={t?.form} matures={t?.maturesAt} lost={t?.lostCores} charge={p.TryGetComp<CompCursedCorpse>()?.charge} message={r?.message} delivered={r?.delivered}");
                if (a?.Pilot != null) Log.Message($"[JJK Advanced Puppet QA] Pilot food={a.Pilot.needs?.food?.CurLevel}; Pigeon bolts={Find.CurrentMap.listerThings.AllThings.OfType<Projectile_PigeonViola>().Count()}");
            }
            foreach (var p in Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringFull == "Tracking Target QA"))
                Log.Message($"[JJK Advanced Puppet QA] Target {p.Position}: moving={p.pather.MovingNow} injuries={p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity)}");
        }
    }
}
