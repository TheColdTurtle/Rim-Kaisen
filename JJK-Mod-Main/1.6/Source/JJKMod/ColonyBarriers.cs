using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JJKMod
{
    public class CompProperties_DangerousRelic : CompProperties
    { public CompProperties_DangerousRelic() { compClass = typeof(CompDangerousRelic); } }
    public class CompDangerousRelic : ThingComp
    {
        public int exposure;
        public override void PostExposeData() { base.PostExposeData(); Scribe_Values.Look(ref exposure, "uncontainedExposure"); }
        public override void CompTickRare()
        {
            if (!parent.Spawned || !parent.Map.IsPlayerHome) return;
            if (parent.Position.GetThingList(parent.Map).Any(t => t is Building_ContainmentVault)) { exposure = 0; return; }
            exposure += 250;
            if (exposure < 120000) return;
            exposure = 0;
            var victim = parent.Map.mapPawns.FreeColonistsSpawned.RandomElementWithFallback();
            if (victim != null && !victim.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("JJK_VaultNightmares")))
                victim.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_VaultNightmares"));
            CursedSpiritUtil.SpawnPack(parent.Map, parent.Position, 100, 3);
            Find.LetterStack.ReceiveLetter("Uncontained cursed relic", "The loose relic has attracted curses. Haul it into a maintained containment vault.", LetterDefOf.ThreatSmall, parent);
        }
    }
    public static class ColonyLifeUtility
    {
        public static bool Available(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed
            && !p.InMentalState && p.Faction == Faction.OfPlayer;
        public static bool Energy(Pawn p, float amount, bool spend = false)
        {
            var ce = p?.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (ce == null || ce.energy < amount) return false;
            if (spend) ce.energy -= amount;
            return true;
        }
        public static Command_Action Command(string label, string desc, System.Action action)
            => new Command_Action { defaultLabel = label, defaultDesc = desc, icon = TexCommand.DesirePower, action = action };
        public static bool Pay(Map map, ThingDef def, int count)
        {
            if (map == null) return false;
            var stacks = map.listerThings.ThingsOfDef(def).Where(t => !t.IsForbidden(Faction.OfPlayer)
                && t.Position.InBounds(map) && map.areaManager.Home[t.Position]).ToList();
            if (stacks.Sum(t => t.stackCount) < count) return false;
            foreach (var t in stacks)
            {
                int take = Mathf.Min(count, t.stackCount); t.SplitOff(take).Destroy(); count -= take;
                if (count == 0) break;
            }
            return true;
        }
        public static void Reject(string message) => Messages.Message(message, MessageTypeDefOf.RejectInput, false);
    }

    public class Building_ContainmentVault : Building_Storage
    {
        public float seal = 1f;
        public int nextBreach;
        public int Relics => slotGroup?.HeldThings.Where(t => t.def.defName == "JJK_DangerousRelic"
            || t.def.defName == "JJK_SealedMissionObject" || t.def.defName == "JJK_SukunaFinger").Sum(t => t.stackCount) ?? 0;
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref seal, "seal", 1f); Scribe_Values.Look(ref nextBreach, "nextBreach");
        }
        public override string GetInspectString() => base.GetInspectString() + "\nSeal integrity: " + seal.ToStringPercent()
            + "\nDangerous objects: " + Relics + "\nMaintain below 75%: 20 CE, 600 ticks of Research work.";
        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned || Relics == 0) return;
            seal = Mathf.Max(0, seal - Relics * 0.12f * 250f / 60000f);
            if (seal > 0.5f || Find.TickManager.TicksGame < nextBreach) return;
            nextBreach = Find.TickManager.TicksGame + 60000;
            Breach();
        }
        public void Breach()
        {
            if (!Spawned || Relics == 0) return;
            Pawn victim = Map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead).RandomElementWithFallback();
            string consequence = "Nightmares spread from the vault.";
            if (victim != null)
            {
                var nightmare = DefDatabase<HediffDef>.GetNamed("JJK_VaultNightmares");
                if (!victim.health.hediffSet.HasHediff(nightmare)) victim.health.AddHediff(nightmare);
                if (victim.needs?.rest != null) victim.needs.rest.CurLevel = Mathf.Max(0, victim.needs.rest.CurLevel - 0.3f);
            }
            if (seal <= 0.2f && victim != null && Rand.Chance(0.5f))
            {
                bool seized = victim.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, forced: true, forceWake: true);
                consequence = seized ? victim.LabelShortCap + " has been possessed. Subdue them until the seizure passes."
                    : victim.LabelShortCap + " resisted a possession attempt.";
            }
            if (seal <= 0.05f)
            {
                CursedSpiritUtil.SpawnPack(Map, Position, 100f * Relics, 5);
                consequence += " Curses have escaped the failing seal!";
            }
            Find.LetterStack.ReceiveLetter("Containment seal failing", consequence + " Restore the vault's seals with a sorcerer.", LetterDefOf.ThreatSmall, this);
        }
        public bool Maintain(Pawn p)
        {
            if (!ColonyLifeUtility.Available(p) || p.Map != Map || !p.CanReach(this, PathEndMode.Touch, Danger.Some)
                || !p.Position.AdjacentTo8WayOrInside(this) || !ColonyLifeUtility.Energy(p, 20, true)) return false;
            seal = 1f; return true;
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var option in base.GetFloatMenuOptions(selPawn)) yield return option;
            if (ColonyLifeUtility.Available(selPawn) && ColonyLifeUtility.Energy(selPawn, 20)
                && selPawn.CanReserveAndReach(this, PathEndMode.Touch, Danger.Some))
                yield return new FloatMenuOption("Maintain containment seals (20 CE)", () => selPawn.jobs.TryTakeOrderedJob(
                    JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_MaintainVault"), this)));
        }
    }
    public class WorkGiver_MaintainVault : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("JJK_ContainmentVault"));
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => t is Building_ContainmentVault vault
            && vault.Faction == Faction.OfPlayer && vault.Relics > 0 && vault.seal < 0.75f && !pawn.Drafted
            && ColonyLifeUtility.Available(pawn) && ColonyLifeUtility.Energy(pawn, 20)
            && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
            => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("JJK_MaintainVault"), t);
    }
    public class JobDriver_MaintainVault : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, errorOnFailed: errorOnFailed);
        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !ColonyLifeUtility.Available(pawn) || !ColonyLifeUtility.Energy(pawn, 20));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => (TargetA.Thing as Building_ContainmentVault)?.Maintain(pawn));
        }
    }

    public enum CurtainRestriction { Curses, Hostiles, Everyone }
    public class Building_CurtainAnchor : Building
    {
        public int channel;
        public CurtainRestriction restriction;
        public bool enabled;
        public CompRefuelable Fuel => GetComp<CompRefuelable>();
        public bool Active => Spawned && Faction == Faction.OfPlayer && enabled && Fuel?.HasFuel == true;
        public float DailyCost => restriction == CurtainRestriction.Curses ? 2f : restriction == CurtainRestriction.Hostiles ? 4f : 6f;
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref channel, "curtainChannel");
            Scribe_Values.Look(ref restriction, "curtainRestriction"); Scribe_Values.Look(ref enabled, "curtainEnabled");
        }
        public override void TickRare()
        {
            base.TickRare();
            if (Active) Fuel.ConsumeFuel(DailyCost * 250f / 60000f);
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var manager = Map?.GetComponent<MapComponent_PlayerCurtains>();
            manager?.RemoveFor(this); base.DeSpawn(mode);
        }
        public override string GetInspectString() => base.GetInspectString() + "\nChannel " + (channel + 1)
            + ": " + restriction + "\n" + (Active ? "Active" : "Inactive") + " — " + DailyCost + " residue/day per anchor"
            + "\nLinks aligned anchors within 40 cells. Structures leave gaps.";
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return ColonyLifeUtility.Command(enabled ? "Lower curtain" : "Raise curtain", "Consumes cursed residue. Build aligned anchors to enclose an area.",
                () => { enabled = !enabled; Map.GetComponent<MapComponent_PlayerCurtains>().Rebuild(); });
            yield return ColonyLifeUtility.Command("Restriction: " + restriction, "Cycle blocked pawns: curses (2/day), hostiles (4/day), everyone (6/day). All anchors in a link must match.",
                () => { restriction = (CurtainRestriction)(((int)restriction + 1) % 3); Map.GetComponent<MapComponent_PlayerCurtains>().Rebuild(); });
            yield return ColonyLifeUtility.Command("Channel: " + (channel + 1), "Four independent curtain networks. Link only anchors with matching channel and restriction.",
                () => { channel = (channel + 1) % 4; Map.GetComponent<MapComponent_PlayerCurtains>().Rebuild(); });
        }
    }
    public class Building_PlayerCurtain : Building_Door
    {
        public Building_CurtainAnchor first, second;
        public bool Active => first?.Active == true && second?.Active == true
            && first.channel == second.channel && first.restriction == second.restriction;
        public bool Blocks(Pawn p) => Active && p != null && (first.restriction == CurtainRestriction.Everyone
            || first.restriction == CurtainRestriction.Curses && p.def.GetModExtension<CursedSpiritExtension>() != null
            || first.restriction == CurtainRestriction.Hostiles && p.HostileTo(Faction.OfPlayer));
        public override bool PawnCanOpen(Pawn p) => !Blocks(p);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_References.Look(ref first, "firstAnchor"); Scribe_References.Look(ref second, "secondAnchor");
        }
        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (Graphic?.MatSingleFor(this) is Material mat)
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc, Quaternion.identity, Vector3.one), mat, 0);
        }
    }
    public class MapComponent_PlayerCurtains : MapComponent
    {
        public MapComponent_PlayerCurtains(Map map) : base(map) { }
        public IEnumerable<Building_PlayerCurtain> Segments => map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_PlayerCurtain")).OfType<Building_PlayerCurtain>();
        public void RemoveFor(Building_CurtainAnchor a)
        {
            foreach (var s in Segments.Where(s => s.first == a || s.second == a).ToList()) s.Destroy();
        }
        public override void MapComponentTick() { if (Find.TickManager.TicksGame % 250 == 0) Rebuild(); }
        public void Rebuild()
        {
            var anchors = map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("JJK_CurtainAnchor"))
                .OfType<Building_CurtainAnchor>().Where(a => a.Active).OrderBy(a => a.thingIDNumber).ToList();
            var desired = new Dictionary<IntVec3, KeyValuePair<Building_CurtainAnchor, Building_CurtainAnchor>>();
            foreach (var a in anchors)
            foreach (var b in anchors.Where(b => b.thingIDNumber > a.thingIDNumber && b.channel == a.channel && b.restriction == a.restriction))
            {
                IntVec3 d = b.Position - a.Position;
                int distance = Mathf.Abs(d.x) + Mathf.Abs(d.z);
                if (d.x != 0 && d.z != 0 || distance < 2 || distance > 40) continue;
                IntVec3 step = new IntVec3(System.Math.Sign(d.x), 0, System.Math.Sign(d.z));
                for (int i = 1; i < distance; i++)
                {
                    var cell = a.Position + step * i;
                    if (!desired.ContainsKey(cell)) desired.Add(cell, new KeyValuePair<Building_CurtainAnchor, Building_CurtainAnchor>(a, b));
                }
            }
            foreach (var s in Segments.ToList())
            {
                if (!desired.TryGetValue(s.Position, out var pair) || s.first != pair.Key || s.second != pair.Value) s.Destroy();
                else desired.Remove(s.Position);
            }
            foreach (var pair in desired)
            {
                // Never overwrite a wall, door, item, or pawn to raise a barrier.
                if (!pair.Key.Walkable(map) || pair.Key.GetEdifice(map) != null || pair.Key.GetThingList(map).Any(t => t is Pawn || t.def.category == ThingCategory.Item)) continue;
                var s = (Building_PlayerCurtain)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_PlayerCurtain"));
                s.first = pair.Value.Key; s.second = pair.Value.Value; s.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(s, pair.Key, map);
            }
        }
    }
    // Door animation may remain open after an allowed pawn crosses. Enforce restrictions at movement time too.
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_PlayerCurtainMovement
    {
        public static bool Prefix(Pawn_PathFollower __instance)
        {
            Pawn p = __instance.pawn;
            if (p?.Spawned != true || !__instance.nextCell.InBounds(p.Map)) return true;
            if (!(__instance.nextCell.GetEdifice(p.Map) is Building_PlayerCurtain s) || !s.Blocks(p)) return true;
            p.pather.StopDead(); return false;
        }
    }
}
