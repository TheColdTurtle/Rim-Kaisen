using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace JJKMod
{
    // Deep-owned pawns preserve identity, injuries, training and boss comp state.
    // The old kind-only list is read for migration, then materialized once.
    public class AbsorbedCurseData : IExposable, IThingHolder
    {
        public const int MaxStored = 25;
        public List<PawnKindDef> kinds = new List<PawnKindDef>();
        public Pawn owner;
        private ThingOwner<Pawn> held;
        public string releaseId;
        public List<Pawn> deployed = new List<Pawn>();
        public bool selectiveSacrifice;
        private HashSet<string> sacrifices = new HashSet<string>();
        public AbsorbedCurseData() { held = new ThingOwner<Pawn>(this); }
        public IThingHolder ParentHolder => owner;
        public ThingOwner GetDirectlyHeldThings() => held;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, held);
        public List<Pawn> Pawns => held.InnerListForReading.ToList();
        public int Count => held.Count + kinds.Count;
        public bool Full => Count >= MaxStored;
        public bool Included(Pawn p) => !selectiveSacrifice || sacrifices.Contains(p.ThingID);
        public int SelectedCount => held.InnerListForReading.Count(p => Included(p));
        public float SelectedPower => held.InnerListForReading.Where(Included).Sum(p => p.kindDef.combatPower);
        public void SelectSacrifice(Pawn p, bool value)
        {
            if (!selectiveSacrifice) { sacrifices = new HashSet<string>(held.InnerListForReading.Select(c => c.ThingID)); selectiveSacrifice = true; }
            if (value) sacrifices.Add(p.ThingID); else sacrifices.Remove(p.ThingID);
        }
        public void MaterializeLegacy()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || kinds.Count == 0 || owner == null) return;
            foreach (var kind in kinds.Where(k => k != null).ToList())
                held.TryAdd(PawnGenerator.GeneratePawn(kind, owner.Faction));
            kinds.Clear();
        }
        public bool Capture(Pawn curse)
        {
            if (curse == null || curse.Dead || curse.Destroyed || Full || owner == null || !curse.Spawned) return false;
            curse.mindState?.mentalStateHandler?.CurState?.RecoverFromState();
            curse.jobs?.StopAll();
            if (curse.Faction != owner.Faction) curse.SetFaction(owner.Faction, owner);
            curse.DeSpawn();
            deployed.Remove(curse);
            return held.TryAdd(curse);
        }
        public Pawn Release(IntVec3 cell, Map map)
        {
            MaterializeLegacy();
            if (map == null || !cell.InBounds(map) || held.Count == 0) return null;
            Pawn chosen = held.InnerListForReading.FirstOrDefault(p => p.ThingID == releaseId)
                ?? held.InnerListForReading.OrderByDescending(p => p.kindDef.combatPower).First();
            if (!held.TryDrop(chosen, cell, map, ThingPlaceMode.Near, out Pawn released)) return null;
            sacrifices.Remove(released.ThingID);
            deployed.Add(released);
            if (released.playerSettings != null) released.playerSettings.followDrafted = true;
            OrderDeployed();
            return released;
        }
        public void OrderDeployed()
        {
            if (owner == null || owner.Dead || !owner.Spawned) return;
            deployed.RemoveAll(p => p == null || p.Destroyed || p.Dead);
            foreach (Pawn curse in deployed)
            {
                if (!curse.Spawned || curse.Map != owner.Map || curse.Downed || curse.Faction != owner.Faction
                    || curse.TryGetComp<CompBossPattern>()?.Busy == true) continue;
                Pawn enemy = curse.Map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(owner)
                    && p.Position.DistanceToSquared(curse.Position) <= 625
                    && curse.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(p => p.Position.DistanceToSquared(curse.Position)).FirstOrDefault();
                if (enemy != null)
                {
                    if (curse.CurJob?.def == JobDefOf.AttackMelee && curse.CurJob.targetA.Pawn == enemy) continue;
                    Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, enemy);
                    job.expiryInterval = 600; job.checkOverrideOnExpire = true;
                    curse.jobs.TryTakeOrderedJob(job);
                }
                else if (owner.Drafted && curse.Position.DistanceToSquared(owner.Position) > 100 && curse.CurJob?.def != JobDefOf.Goto)
                    curse.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,
                        CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 4)));
            }
        }
        public float ConsumeSelected(out int count)
        {
            MaterializeLegacy();
            var consumed = held.InnerListForReading.Where(Included).ToList();
            count = consumed.Count;
            float power = consumed.Sum(p => p.kindDef.combatPower);
            foreach (Pawn p in consumed) { held.Remove(p); sacrifices.Remove(p.ThingID); p.Destroy(); }
            return power;
        }
        public void ReleaseOnOwnerLoss(Map map, IntVec3 cell)
        {
            if (map == null) return;
            MaterializeLegacy();
            foreach (Pawn p in held.InnerListForReading.ToList())
                if (held.TryDrop(p, cell, map, ThingPlaceMode.Near, out Pawn released))
                {
                    released.SetFaction(null);
                    released.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
                }
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref owner, "rosterOwner");
            if (Scribe.mode == LoadSaveMode.Saving) deployed.RemoveAll(p => p == null || p.Destroyed);
            Scribe_Collections.Look(ref deployed, "deployed", LookMode.Reference);
            Scribe_Collections.Look(ref kinds, "kinds", LookMode.Def);
            Scribe_Deep.Look(ref held, "capturedPawns", this);
            Scribe_Values.Look(ref releaseId, "releaseId");
            Scribe_Values.Look(ref selectiveSacrifice, "selectiveSacrifice");
            Scribe_Collections.Look(ref sacrifices, "sacrifices", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (held == null) held = new ThingOwner<Pawn>(this);
                if (kinds == null) kinds = new List<PawnKindDef>();
                kinds.RemoveAll(k => k == null);
                if (sacrifices == null) sacrifices = new HashSet<string>();
                if (deployed == null) deployed = new List<Pawn>();
                deployed.RemoveAll(p => p == null);
            }
        }
    }

    // Release immediately, before a destroyed owner can be pruned from saved progress.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Patch_CurseOwnerDestroyed
    {
        public static void Prefix(Pawn __instance, out Map __state) => __state = __instance.MapHeld;
        public static void Postfix(Pawn __instance, Map __state)
        {
            Map map = __state ?? Find.AnyPlayerHomeMap;
            if (__instance.Destroyed && map != null)
                GameComponent_JJK.Instance?.ExistingCurseRoster(__instance)?.ReleaseOnOwnerLoss(map,
                    __instance.Position.InBounds(map) ? __instance.Position : map.Center);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CurseOwnerKilled
    {
        public static void Prefix(Pawn __instance, out Map __state) => __state = __instance.MapHeld;
        public static void Postfix(Pawn __instance, Map __state)
        {
            if (!__instance.Dead) return;
            Map map = __state ?? __instance.Corpse?.Map ?? Find.AnyPlayerHomeMap;
            if (map != null) GameComponent_JJK.Instance?.ExistingCurseRoster(__instance)?.ReleaseOnOwnerLoss(map,
                __instance.Corpse?.Position ?? map.Center);
        }
    }
}
