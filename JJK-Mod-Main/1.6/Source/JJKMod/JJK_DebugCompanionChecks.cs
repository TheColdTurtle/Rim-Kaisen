using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using TaranMagicFramework;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public static class JJK_DebugCompanionChecks
    {
        private static Pawn Human(Map map, IntVec3 cell, string name)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);
            p.Name = new NameSingle(name); GenSpawn.Spawn(p, cell, map); return p;
        }
        private static CompIndependentCompanion Unit(Pawn maker, IntVec3 cell, string name, bool mature = true)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_PandaConstruct_Kind"), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 0, fixedChronologicalAge: 0, fixedGender: Gender.None));
            p.Name = new NameSingle(name); var core = p.TryGetComp<CompCursedCorpse>(); core.controller = maker; core.charge = 120; core.follow = false;
            GenSpawn.Spawn(p, cell, maker.Map); if (mature) p.TryGetComp<CompThreeCores>().maturesAt = 0;
            return p.TryGetComp<CompIndependentCompanion>();
        }
        [DebugAction("Rim-Kaisen", "Run companion checks", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            var made = new List<Pawn>(); int pass = 0, fail = 0;
            Action<bool,string> check = (ok, name) => { if (ok) pass++; else fail++; Log.Message("[JJK Companion QA] " + (ok ? "PASS " : "FAIL ") + name); };
            try
            {
                var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
                var friend = Human(map, cell, "Friend QA"); made.Add(friend);
                var c = Unit(friend, cell + IntVec3.East * 2, "Companion QA", false); made.Add(c.Unit);
                check(!c.Independent && c.Routine() == null && !c.Visit(friend), "immature cores cannot use independent routines");
                c.Cores.maturesAt = 0;
                check(c.Independent && c.companion == friend && c.bond == 20, "mature companion keeps identity and initial maker bond");
                check(GameComponent_Puppets.Instance.Count(friend) == 0, "mature companion costs no control slot");
                c.Core.controller = null; check(c.CanAct && c.companion == friend, "friendship survives loss of controller link");
                c.temperament = CompanionTemperament.Gentle;
                check(c.Visit(friend) && c.bond == 28, "real nearby visit increases gentle bond by eight");
                check(friend.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "JJK_CompanionCompany"), "visit gives real colonist memory");
                check(!c.Visit(friend) && c.bond == 28, "social cooldown rejects repeat benefits");
                c.nextSocial = 0; c.bond = 59; check(c.Visit(friend) && c.bond == 67, "bond reaches trusted threshold");
                check(friend.needs.mood.thoughts.memories.Memories.Count(m => m.def.defName.StartsWith("JJK_Companion") && m.def.defName.Contains("Company")) == 1
                    && friend.needs.mood.thoughts.memories.Memories.Any(m => m.def.defName == "JJK_CompanionTrustedCompany"), "trusted company replaces ordinary benefit without stacking");
                c.nextSocial = 0; c.bond = 99; c.Visit(friend); check(c.bond == 100, "trust capped at 100");
                c.Core.standby = true; check(c.Routine() == null && !c.Visit(friend), "standby blocks routines and social benefits"); c.Core.standby = false;
                c.Core.destination = c.Unit.Position; check(c.Routine() == null, "manual hold overrides independent routines");
                c.SetRole(CompanionRole.Guard); check(!c.Core.destination.IsValid && c.role == CompanionRole.Guard && c.Routine() == null, "role selection releases hold and guard does not wander");
                int now = Find.TickManager.TicksGame; c.temperament = CompanionTemperament.Steadfast;
                c.Cores.lostCores |= 2; c.UpdateEmotions(now);
                check(c.griefUntil == now + 120000 && c.Grieving && c.CanAct, "core loss causes shorter steadfast grief without shutting down");
                c.UpdateEmotions(now + 100); check(c.griefUntil == now + 120000, "old core loss does not refresh grief indefinitely");
                c.SetRole(CompanionRole.Hauler); c.nextSocial = 0;
                check(c.Routine()?.def.defName == "JJK_CompanionVisit", "grieving worker seeks company before work");
                c.nextSocial = 0; int until = c.griefUntil; c.Visit(friend); check(c.griefUntil == until - 5000, "company eases construct grief");
                friend.Kill(null); c.UpdateEmotions(now);
                check(c.lossRemembered && c.rememberedName == "Friend QA" && c.CanAct, "friend death remembered while companion remains autonomous");
                float after = c.bond; c.UpdateEmotions(now + 60); check(c.bond == after, "death loss is applied once");
                var replacement = Human(map, cell, "New Friend QA"); made.Add(replacement);
                int cooldown = c.nextSocial; until = c.griefUntil;
                check(c.Befriend(replacement) && c.bond == 20 && !c.lossRemembered && c.nextSocial == cooldown && c.griefUntil == until, "new friendship does not erase grief or reset cooldown");
                check(!c.Befriend(replacement) && !c.Befriend(null), "same or null friend rejected");
                c.Unit.Kill(null); c.NotifyDeath();
                check(replacement.needs.mood.thoughts.memories.Memories.Count(m => m.def.defName == "JJK_CompanionLost") == 1, "construct death gives one mourning memory");
            }
            catch (Exception e) { fail++; Log.Error("[JJK Companion QA] " + e); }
            finally { foreach (var p in made) if (!p.Destroyed) p.Destroy(); Log.Message($"[JJK Companion QA] RESULT {pass} passed, {fail} failed"); }
        }
        [DebugAction("Rim-Kaisen", "Prepare companion jobs QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Prepare()
        {
            var map = Find.CurrentMap; var cell = JJK_DebugAdvancedPuppetChecks.Clear(map);
            var owner = Human(map, cell, "Companion Friend QA");
            var social = Unit(owner, cell + IntVec3.West * 2, "Social Companion QA"); social.SetRole(CompanionRole.Companion);
            var haul = Unit(owner, cell + IntVec3.East * 4, "Hauler Companion QA"); haul.SetRole(CompanionRole.Hauler); haul.nextSocial = int.MaxValue;
            var steel = ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount = 13; GenSpawn.Spawn(steel, cell + IntVec3.East * 5, map); steel.SetForbidden(false);
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager); map.zoneManager.RegisterZone(zone);
            foreach (var c in CellRect.CenteredOn(cell + IntVec3.North * 7, 1)) if (map.zoneManager.ZoneAt(c) == null) zone.AddCell(c);
            zone.settings.filter.SetDisallowAll(); zone.settings.filter.SetAllow(ThingDefOf.Steel, true); zone.settings.Priority = StoragePriority.Critical;
            var patient = Human(map, cell + IntVec3.South * 3, "Companion Patient QA"); patient.health.AddHediff(HediffDefOf.Anesthetic);
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog); bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, cell + IntVec3.South * 7, map); bed.Medical = true;
            var rescue = Unit(owner, cell + IntVec3.South * 4, "Rescuer Companion QA"); rescue.SetRole(CompanionRole.Rescuer); rescue.nextSocial = int.MaxValue;
            var repair = Unit(owner, cell + IntVec3.West * 6, "Repair Companion QA"); repair.SetRole(CompanionRole.Guard);
            var wound = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Crack"), repair.Unit, repair.Unit.RaceProps.body.corePart); wound.Severity = 10; repair.Unit.health.AddHediff(wound);
            var kit = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_PuppetRepairKit")); GenSpawn.Spawn(kit, cell + IntVec3.West * 7, map); kit.SetForbidden(false);
            Log.Message($"[JJK Companion QA] FIXTURE center={cell} steel={steel.ThingID} bed={bed.ThingID} kit={kit.ThingID}");
            Report();
        }
        [DebugAction("Rim-Kaisen", "Report companion jobs QA", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report()
        {
            foreach (var p in Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p.Name?.ToStringFull.Contains("Companion") == true))
            {
                var c = p.TryGetComp<CompIndependentCompanion>();
                Log.Message($"[JJK Companion QA] LIVE {p.Name} id={p.ThingID} pos={p.Position} job={p.CurJob?.def.defName} target={p.CurJob?.targetA} bed={p.CurrentBed()?.ThingID} bond={c?.bond} friend={c?.companion?.ThingID} role={c?.role} damaged={c?.CanRepair}");
            }
        }
    }
}
