using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Verse.AI;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    // Geometry of a domain: a flood-fill from the caster that molds around existing
    // structures, plus the ring of open-gap cells that need sealing.
    internal static class DomainGeometry
    {
        // The interior is every cell reachable from the centre, within radius,
        // without crossing a wall or doorway - so a domain cast in a room fills only
        // that room, and one cast in the open spreads to the full radius. 4-way
        // connectivity prevents the fill leaking diagonally past a wall corner.
        public static HashSet<IntVec3> ComputeInterior(Map map, IntVec3 center, float radius)
        {
            var interior = new HashSet<IntVec3>();
            if (map == null || !center.InBounds(map)) return interior;

            float r2 = radius * radius;
            var q = new Queue<IntVec3>();
            interior.Add(center);
            q.Enqueue(center);
            while (q.Count > 0)
            {
                IntVec3 c = q.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (interior.Contains(n) || !n.InBounds(map)) continue;
                    if ((n - center).LengthHorizontalSquared > r2) continue;
                    if (!n.Walkable(map)) continue;          // stop at walls / rock
                    if (n.GetDoor(map) != null) continue;    // seal at doorways too
                    interior.Add(n);
                    q.Enqueue(n);
                }
            }
            return interior;
        }

        // An interior cell needs a barrier if any cardinal neighbour is an OPEN
        // exterior gap (walkable / a doorway / the map edge). Where the neighbour is
        // an existing wall, that edge is already sealed and no barrier is placed -
        // so the domain wall molds around structures and never replaces them.
        public static bool IsOpenEdge(Map map, HashSet<IntVec3> interior, IntVec3 c)
        {
            for (int i = 0; i < 4; i++)
            {
                IntVec3 n = c + GenAdj.CardinalDirections[i];
                if (interior.Contains(n)) continue;
                if (!n.InBounds(map) || n.Walkable(map) || n.GetDoor(map) != null) return true;
            }
            return false;
        }

        public static List<IntVec3> ComputeBarrierCells(Map map, HashSet<IntVec3> interior)
        {
            var cells = new List<IntVec3>();
            foreach (IntVec3 c in interior)
                if (IsOpenEdge(map, interior, c)) cells.Add(c);
            return cells;
        }
    }

    // One open Domain Expansion: the caster, where it manifested, and which
    // technique opened it (so its DomainExtension can be re-read each tick).
    public class ActiveDomain : IExposable
    {
        public Pawn caster;
        public IntVec3 center;
        public AbilityDef sourceAbility;
        public int ageTicks;

        // Simple Domain only: remaining durability before it shatters under a
        // hostile domain's sure-hit. Set from props.simpleDomainIntegrity at cast.
        public float integrity;
        public DomainRefinement refinement;
        public float Radius => (Props?.radius ?? 15f) * (refinement == DomainRefinement.Condensed ? .65f : 1f);
        public float RefinementPower => refinement == DomainRefinement.Condensed ? 1.35f : refinement == DomainRefinement.Economical ? .85f : 1f;
        public float RefinementUpkeep => refinement == DomainRefinement.Economical ? .65f : refinement == DomainRefinement.Fortified ? 1.2f : 1f;

        // Temporary shikigami this domain manifested (destroyed on collapse).
        public List<Pawn> summons = new List<Pawn>();
        // The closed-domain barrier buildings (destroyed on collapse).
        public List<Thing> barriers = new List<Thing>();

        // The technique tree locked while this domain is up / on burnout.
        public AbilityTreeDef Tree =>
            sourceAbility?.abilityTrees != null && sourceAbility.abilityTrees.Count > 0
                ? sourceAbility.abilityTrees[0]
                : null;

        public DomainExtension Props => sourceAbility?.GetModExtension<DomainExtension>();

        // Flood-filled interior cells (not saved - recomputed on demand). The
        // snapshot is taken at cast time and held for the domain's life.
        private HashSet<IntVec3> interiorCache;

        public HashSet<IntVec3> Interior(Map map)
        {
            if (interiorCache == null)
                interiorCache = DomainGeometry.ComputeInterior(map, center, Radius);
            return interiorCache;
        }

        public bool Contains(Map map, IntVec3 c) => Interior(map).Contains(c);

        public void ExposeData()
        {
            Scribe_References.Look(ref caster, "caster");
            Scribe_Defs.Look(ref sourceAbility, "sourceAbility");
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref integrity, "integrity");
            Scribe_Values.Look(ref refinement, "refinement");
            Scribe_Collections.Look(ref summons, "summons", LookMode.Reference);
            Scribe_Collections.Look(ref barriers, "barriers", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (summons == null) summons = new List<Pawn>();
                if (barriers == null) barriers = new List<Thing>();
                summons.RemoveAll(p => p == null);
                barriers.RemoveAll(b => b == null);
            }
        }
    }

    // The single source of truth for Domain Expansion on a map. It owns every open
    // domain's lifetime, geometry (interior + barrier), the sure-hit pulse, the
    // caster's in-domain buff, cursed-energy upkeep, burnout, the temporary
    // shikigami, and the heavenly-restriction phase-through. The Ability just
    // registers a domain here; the in-domain hediff's gizmo asks it to collapse.
    public class MapComponent_DomainManager : MapComponent
    {
        private List<ActiveDomain> domains = new List<ActiveDomain>();
        public IReadOnlyList<ActiveDomain> AllDomains => domains;

        // The in-domain buff hediff stage the caster sits at (always inside).
        private const float SeverityInside = 1f;

        public MapComponent_DomainManager(Map map) : base(map) { }

        // The "in-domain" hediff a domain puts on its caster - the offensive buff
        // for a full domain, or the light defensive buff for a Simple Domain.
        private static HediffDef ActiveHediffDef(DomainExtension props) =>
            props?.activeHediff ?? JJK_DefOf.JJK_DomainActive;

        public bool HasDomain(Pawn caster) => DomainFor(caster) != null;

        // True if the pawn is currently immune to hostile domain sure-hit.
        //
        // Three anti-domain answers feed in here, and they are deliberately different:
        //  - SIMPLE DOMAIN: an area that shelters the caster AND their allies, worn
        //    down over time by the attacking domain's sureHitStrain.
        //  - DOMAIN AMPLIFICATION: a thin layer of one's own domain worn on the body.
        //    Selfish (protects only the wearer) and expensive to hold, but it does not
        //    degrade while it is up.
        // Falling Blossom Emotion is NOT checked here - it is a one-shot parry that has
        // to be consumed at the moment it turns a hit aside, so it lives in Pulse().
        // Kept side-effect free: HostilesInside is also used for summon targeting.
        private bool IsShieldedFromSureHit(Pawn p)
        {
            if (p == null) return false;
            if (p.TryGetComp<CompAbsolutePuppet>()?.Shielded == true) return true;

            if (DomainAmplificationUtil.Active(p))
                return true;

            for (int i = 0; i < domains.Count; i++)
            {
                ActiveDomain d = domains[i];
                DomainExtension props = d.Props;
                if (props == null || !props.isSimpleDomain || d.caster == null) continue;
                if (p == d.caster) return true;
                if (!p.HostileTo(d.caster) && d.Contains(map, p.Position)) return true;
            }
            return false;
        }

        // Falling Blossom Emotion: a single perfect parry. Unlike Simple Domain it wards
        // nothing over time - it shatters the instant it turns aside one sure-hit, which
        // is why it is cheap, instant to raise, and needs no grade.
        private bool TryConsumeFallingBlossom(Pawn p)
        {
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_FallingBlossomEmotion);
            if (h == null) return false;
            p.health.RemoveHediff(h);
            if (p.Spawned && p.Map != null)
                MoteMaker.ThrowText(p.DrawPos, p.Map, "JJK_FallingBlossomShattered".Translate(), 3.5f);
            return true;
        }

        // True if the pawn is a temporary shikigami manifested by an open domain
        // (used by the Pawn.Kill patch to vanish its corpse on death).
        public bool IsDomainSummon(Pawn pawn)
        {
            for (int i = 0; i < domains.Count; i++)
                if (domains[i].summons.Contains(pawn)) return true;
            return false;
        }

        // True if 'instigator' is attacking 'victim' as part of a hostile domain the
        // victim is trapped inside (its caster or one of its summons, victim inside
        // the interior, enemies). A domain's sure-hit bypasses the victim's Infinity.
        // Heavenly-restriction phasers are immune to the domain, so never affected.
        public bool IsHostileDomainAttack(Pawn victim, Pawn instigator)
        {
            if (victim == null || instigator == null) return false;
            if (JJK_Utils.CanPhaseDomainBarrier(victim)) return false;
            for (int i = 0; i < domains.Count; i++)
            {
                ActiveDomain d = domains[i];
                if (d.caster == null || !victim.HostileTo(d.caster)) continue;
                if (instigator != d.caster && !d.summons.Contains(instigator)) continue;
                if (d.Contains(map, victim.Position)) return true;
            }
            return false;
        }

        public ActiveDomain DomainFor(Pawn caster)
        {
            for (int i = 0; i < domains.Count; i++)
                if (domains[i].caster == caster) return domains[i];
            return null;
        }

        // Open a domain centred on the caster. Called from Ability_DomainExpansion.
        public void Open(Pawn caster, AbilityDef sourceAbility)
        {
            if (caster == null || sourceAbility == null) return;
            if (HasDomain(caster)) return;
            if (sourceAbility.GetModExtension<DomainExtension>()?.gambling == true)
            {
                var state = ExpansionTech.State(caster);
                if (!state.Operational || state.jackpotUntil > ExpansionTech.Now) return;
                state.gambleStart = 0;
            }

            ActiveDomain domain = new ActiveDomain
            {
                caster = caster,
                center = caster.Position,
                sourceAbility = sourceAbility,
                ageTicks = 0
            };
            DomainExtension openingProps = sourceAbility.GetModExtension<DomainExtension>();
            if (openingProps?.isSimpleDomain == false) domain.refinement = SorceryDevelopment.State(caster)?.Refinement(domain.Tree) ?? DomainRefinement.None;
            if (openingProps != null && openingProps.isSimpleDomain)
                domain.integrity = openingProps.simpleDomainIntegrity * MasteryFactor(domain);
            domains.Add(domain);

            DomainExtension props = domain.Props;
            HediffDef activeDef = ActiveHediffDef(props);
            if (caster.health != null
                && caster.health.hediffSet.GetFirstHediffOfDef(activeDef) == null)
            {
                Hediff h = caster.health.AddHediff(activeDef);
                h.Severity = SeverityInside;
            }

            // A new Domain Expansion that overlaps a hostile one clashes at once -
            // resolved before raising the barrier/summons so a domain crushed in the
            // clash never wastefully manifests them. Simple Domains do not clash.
            if (props != null && !props.isSimpleDomain) CheckClash(domain);
            if (!domains.Contains(domain)) return; // lost the clash on contact

            if (props != null && props.closed) RaiseBarrier(domain, props);
            SpawnSummons(domain, props);
            if(props?.gambling==true)GameComponent_TechniqueExpansion.Instance.TickGamble(ExpansionTech.State(caster),domain);

            if (caster.Spawned)
            {
                CombatPresentation.Play(props?.isSimpleDomain == true ? "Healing" : "DomainOpen", map, caster.Position);
                FleckMaker.Static(caster.Position, map, FleckDefOf.PsycastSkipInnerExit, 3f);
            }
        }

        // Effective mastery a caster brings to a domain clash, in that domain's
        // technique. Accumulated mastery XP plus a grade baseline.
        private static int ClashMastery(ActiveDomain d) =>
            Mathf.RoundToInt((GameComponent_JJK.Instance?.GetEffectiveMastery(d.caster, d.Tree) ?? 0)
                * (d.refinement == DomainRefinement.Fortified ? 1.25f : 1f));

        // A newly opened Domain Expansion contests every hostile domain it overlaps.
        // Two domains can only begin overlapping when the second opens (domains are
        // fixed once cast), so checking here is sufficient - no per-tick sweep.
        private void CheckClash(ActiveDomain incoming)
        {
            HashSet<IntVec3> incInterior = incoming.Interior(map);
            List<ActiveDomain> others = new List<ActiveDomain>(domains);
            foreach (ActiveDomain other in others)
            {
                if (other == incoming) continue;
                if (!domains.Contains(incoming)) return; // incoming already shattered
                DomainExtension op = other.Props;
                if (op == null || op.isSimpleDomain || other.caster == null) continue;
                if (incoming.caster == null || !incoming.caster.HostileTo(other.caster)) continue;
                if (!Overlaps(incInterior, other.Interior(map))) continue;
                ResolveClash(incoming, other);
            }
        }

        // Resolve a clash between two overlapping hostile domains. The greater
        // effective mastery wins (loser shatters with full burnout); near-equal
        // mastery is a stalemate that shatters both.
        private void ResolveClash(ActiveDomain a, ActiveDomain b)
        {
            int ma = ClashMastery(a);
            int mb = ClashMastery(b);
            float margin = Mathf.Max(150f, 0.10f * Mathf.Max(ma, mb));

            IntVec3 mid = new IntVec3((a.center.x + b.center.x) / 2, 0, (a.center.z + b.center.z) / 2);
            FleckMaker.Static(mid, map, FleckDefOf.PsycastAreaEffect, 6f);

            if (Mathf.Abs(ma - mb) <= margin)
            {
                ClashMessage("JJK_DomainClashStalemate", a.caster, b.caster);
                Collapse(a, voluntary: false);
                Collapse(b, voluntary: false);
            }
            else
            {
                ActiveDomain winner = ma > mb ? a : b;
                ActiveDomain loser = ma > mb ? b : a;
                ClashMessage("JJK_DomainClashWon", winner.caster, loser.caster);
                Collapse(loser, voluntary: false);
            }
        }

        private void ClashMessage(string key, Pawn winner, Pawn loser)
        {
            if ((winner != null && winner.Faction != null && winner.Faction.IsPlayer)
                || (loser != null && loser.Faction != null && loser.Faction.IsPlayer))
            {
                Messages.Message(key.Translate(winner.LabelShortCap, loser.LabelShortCap),
                    winner ?? loser, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // Raise the closed-domain wall on the open-gap edge cells, molding around
        // existing structures. Pawns standing on a barrier cell are pushed inward
        // so the seal is complete and nobody is trapped inside a wall.
        private void RaiseBarrier(ActiveDomain domain, DomainExtension props)
        {
            HashSet<IntVec3> interior = domain.Interior(map);
            Color tint = props.barrierColor;
            tint.a = 0.5f;

            foreach (IntVec3 c in DomainGeometry.ComputeBarrierCells(map, interior))
            {
                if (c.GetEdifice(map) != null) continue; // never overwrite a real structure
                PushPawnsInward(c, domain, interior);

                Thing barrier = ThingMaker.MakeThing(JJK_DefOf.JJK_DomainBarrier);
                barrier.TryGetComp<CompColorable>()?.SetColor(tint);
                // VanishOrMoveAside relocates loose items under the cell instead of
                // destroying them, so a domain never deletes what it covers.
                GenSpawn.Spawn(barrier, c, map, WipeMode.VanishOrMoveAside);
                domain.barriers.Add(barrier);
            }
        }

        private void PushPawnsInward(IntVec3 c, ActiveDomain domain, HashSet<IntVec3> interior)
        {
            foreach (Thing t in c.GetThingList(map).ToList())
            {
                if (!(t is Pawn p) || p == domain.caster) continue;
                IntVec3 dest = IntVec3.Invalid;
                float best = float.MaxValue;
                foreach (IntVec3 off in GenAdj.AdjacentCells)
                {
                    IntVec3 n = c + off;
                    if (!n.InBounds(map) || !interior.Contains(n) || !n.Standable(map)) continue;
                    if (DomainGeometry.IsOpenEdge(map, interior, n)) continue; // not onto another wall cell
                    float dsq = (n - domain.center).LengthHorizontalSquared;
                    if (dsq < best) { best = dsq; dest = n; }
                }
                if (dest.IsValid)
                {
                    p.Position = dest;
                    p.Notify_Teleported(false, true);
                }
            }
        }

        // Manifest a domain's temporary shikigami (fish, or the caster's tamed
        // shadows). They are plain allied pawns with no shikigami bond, so their
        // destruction on collapse never touches the caster's Ten Shadows roster.
        private void SpawnSummons(ActiveDomain domain, DomainExtension props)
        {
            if (props == null) return;
            Pawn caster = domain.caster;

            // More mastery manifests a larger swarm (summon-based domains).
            float masteryFactor = MasteryFactor(domain);

            if (props.summonKinds != null)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(props.summonCountEach * masteryFactor));
                foreach (PawnKindDef kind in props.summonKinds)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Pawn p = SummonDomainPawn(caster, kind, domain.center);
                        if (p != null) domain.summons.Add(p);
                    }
                }
            }

            if (props.summonTamedShadows)
            {
                TenShadowsData data = GameComponent_JJK.Instance?.GetTenShadows(caster);
                if (data != null)
                {
                    foreach (ShikigamiDef sd in DefDatabase<ShikigamiDef>.AllDefsListForReading)
                    {
                        if (data.GetState(sd) != ShikigamiState.Available) continue;
                        Pawn p = SummonDomainPawn(caster, sd.pawnKind, domain.center);
                        if (p != null) domain.summons.Add(p);
                    }
                }
            }
        }

        private Pawn SummonDomainPawn(Pawn caster, PawnKindDef kind, IntVec3 center)
        {
            if (kind == null || caster?.Map == null) return null;
            Pawn p = PawnGenerator.GeneratePawn(kind, caster.Faction);
            if (p.Faction != caster.Faction) p.SetFaction(caster.Faction, caster);
            IntVec3 c = CellFinder.RandomClosewalkCellNear(center, map, 5);
            GenSpawn.Spawn(p, c, map);
            FleckMaker.Static(c, map, FleckDefOf.PsycastSkipInnerExit, 1.5f);
            return p;
        }

        // Collapse the caster's domain. Voluntary collapse (the gizmo) carries a
        // shorter burnout than an involuntary one (ran dry / caster downed).
        public void CollapseFor(Pawn caster, bool voluntary)
        {
            ActiveDomain d = DomainFor(caster);
            if (d != null) Collapse(d, voluntary);
        }

        private void Collapse(ActiveDomain domain, bool voluntary)
        {
            domains.Remove(domain);
            DomainInteriors.Release(domain);
            CombatPresentation.Play("DomainEnd", map, domain.center);
            DomainExtension props = domain.Props;

            // A stun domain leaves a lingering stun that scales with how long it was
            // held - applied now so it persists after the barrier is gone.
            if (props != null && props.pulseStun)
            {
                int linger = Mathf.RoundToInt(
                    Mathf.Clamp(domain.ageTicks, props.pulseStunTicks, props.maxLingerStunTicks)
                    * MasteryFactor(domain));
                foreach (Pawn p in HostilesInside(domain))
                {
                    using (new MahoragaExposure(MahoragaPhenomena.ForAbility(domain.sourceAbility), domain.caster))
                        p.stances?.stunner?.StunFor(linger, domain.caster);
                }
            }

            // Tear down the barrier wall and return every summon to the shadows.
            RemoveBarriers(domain);
            RemoveSummons(domain);

            Pawn caster = domain.caster;
            if (caster?.health == null) return;

            Hediff active = caster.health.hediffSet.GetFirstHediffOfDef(ActiveHediffDef(props));
            if (active != null) caster.health.RemoveHediff(active);

            // Simple Domain is a basic defensive skill: no post-domain burnout.
            if (props == null || !props.isSimpleDomain)
                ApplyBurnout(caster, props, domain.Tree, voluntary);
        }

        private void RemoveBarriers(ActiveDomain domain)
        {
            foreach (Thing b in domain.barriers)
            {
                if (b != null && b.Spawned && !b.Destroyed) b.Destroy();
            }
            domain.barriers.Clear();
        }

        private void RemoveSummons(ActiveDomain domain)
        {
            foreach (Pawn s in domain.summons)
            {
                if (s != null && s.Spawned && !s.Destroyed) s.Destroy();
            }
            domain.summons.Clear();
        }

        // Hostiles standing inside the domain interior - the targets of its
        // sure-hit (pulse, summons, lingering stun). Two kinds of pawn are exempt:
        // heavenly-restriction phasers (they walk the domain freely), and anyone
        // sheltering in a friendly Simple Domain (its neutralizing counter).
        private IEnumerable<Pawn> HostilesInside(ActiveDomain domain)
        {
            // Damage can despawn targets or recursively collapse another domain.
            // Each iterator owns a pooled snapshot until its caller finishes.
            var snapshot = SimplePool<List<Pawn>>.Get();
            snapshot.AddRange(map.mapPawns.AllPawnsSpawned);
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                    if (IsHostileInside(domain, snapshot[i])) yield return snapshot[i];
            }
            finally { snapshot.Clear(); SimplePool<List<Pawn>>.Return(snapshot); }
        }

        private bool IsHostileInside(ActiveDomain domain, Pawn p) =>
            !p.Dead && p != domain.caster && domain.Contains(map, p.Position)
            && p.HostileTo(domain.caster) && !JJK_Utils.CanPhaseDomainBarrier(p)
            && !IsShieldedFromSureHit(p);

        private void ApplyBurnout(Pawn caster, DomainExtension props, AbilityTreeDef lockedTree, bool voluntary)
        {
            if (caster?.health == null) return;
            int ticks = props != null
                ? (voluntary ? props.burnoutTicksVoluntary : props.burnoutTicksInvoluntary)
                : (voluntary ? 15000 : 60000);
            if (ticks <= 0) return;

            Hediff burnout = caster.health.hediffSet.GetFirstHediffOfDef(JJK_DefOf.JJK_DomainBurnout)
                             ?? caster.health.AddHediff(JJK_DefOf.JJK_DomainBurnout);
            HediffComp_Disappears disappears = burnout.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = ticks;
            HediffComp_DomainBurnout lockComp = burnout.TryGetComp<HediffComp_DomainBurnout>();
            if (lockComp != null) lockComp.lockedTree = lockedTree;
        }

        public override void MapComponentTick()
        {
            TickEnemyDomainAI();

            if (domains.Count == 0) return;

            for (int i = domains.Count - 1; i >= 0; i--)
            {
                ActiveDomain d = domains[i];
                DomainExtension props = d.Props;
                Pawn caster = d.caster;

                if (props == null || caster == null || caster.Dead || caster.Downed
                    || !caster.Spawned || caster.Map != map)
                {
                    Collapse(d, voluntary: false);
                    continue;
                }

                d.ageTicks++;

                // Cursed-energy upkeep, reduced by the caster's efficiency.
                AbilityResource res = caster.GetComp<CompAbilities>()
                    ?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
                if (res != null)
                {
                    float efficiency = Mathf.Max(0.01f,
                        caster.GetStatValue(JJK_DefOf.JJK_CursedEnergyUsageMultiplier));
                    res.energy -= props.upkeepPerTick * efficiency * d.RefinementUpkeep;
                    if (res.energy <= 0f)
                    {
                        res.energy = 0f;
                        Collapse(d, voluntary: false);
                        continue;
                    }
                }

                // Stepping out of the domain collapses it (a closed barrier keeps the
                // caster in, so this mainly guards open domains).
                if (!d.Contains(map, caster.Position))
                {
                    Collapse(d, voluntary: true);
                    continue;
                }

                Hediff active = caster.health?.hediffSet.GetFirstHediffOfDef(ActiveHediffDef(props));
                if (active != null) active.Severity = SeverityInside;

                OrderSummons(d);

                // Simple Domain has no offensive pulse - it only shields.
                if (!props.isSimpleDomain
                    && d.ageTicks % Mathf.Max(1, props.pulseIntervalTicks) == 0)
                {
                    Pulse(d, props);
                    WearDownSimpleDomains(d, props);
                }
            }
        }

        // Direct summons at the nearest hostile inside the domain (and prune any
        // that have died or been destroyed). A summon is re-tasked whenever it is
        // not already attacking a STILL-VALID hostile - so a target that became
        // valid after the fact (e.g. an enemy who dropped their Simple Domain and
        // is now exposed) is picked up promptly rather than ignored.
        private void OrderSummons(ActiveDomain d)
        {
            if (d.summons.Count == 0) return;
            d.summons.RemoveAll(p => p == null || p.Dead || p.Destroyed || !p.Spawned);
            if (d.summons.Count == 0 || d.ageTicks % 30 != 0) return;

            List<Pawn> hostiles = HostilesInside(d).ToList();
            if (hostiles.Count == 0) return;
            HashSet<Pawn> hostileSet = new HashSet<Pawn>(hostiles);

            foreach (Pawn s in d.summons)
            {
                if (s.Downed || s.jobs == null) continue;

                // Leave it be only if it is already attacking a valid hostile.
                JobDef cur = s.CurJobDef;
                if ((cur == JobDefOf.AttackMelee || cur == JobDefOf.AttackStatic)
                    && s.CurJob?.targetA.Thing is Pawn curTarget
                    && hostileSet.Contains(curTarget))
                {
                    continue;
                }

                Pawn target = hostiles.OrderBy(h => h.Position.DistanceToSquared(s.Position)).First();
                OrderAttack(s, target);
            }
        }

        private void OrderAttack(Pawn attacker, Thing target)
        {
            Verb verb = attacker.TryGetAttackVerb(target, false);
            Job job = (verb != null && !verb.IsMeleeAttack)
                ? JobMaker.MakeJob(JobDefOf.AttackStatic, target)
                : JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.killIncappedTarget = true;
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;
            attacker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        // An offensive domain grinds against any hostile Simple Domain overlapping
        // it - but only if the attacker is the stronger sorcerer. Wear scales with
        // how much the attacker's mastery exceeds the defender's: a stronger (or
        // even comparable) defender holds their Simple Domain indefinitely, while a
        // clearly stronger domain (e.g. a Special grade over a novice) grinds it
        // down and shatters it. So a Calamity user's Simple Domain shrugs off a
        // weaker Special domain, but a weak sorcerer's cannot.
        private void WearDownSimpleDomains(ActiveDomain attacker, DomainExtension attackerProps)
        {
            if (attackerProps.sureHitStrain <= 0f || attacker.caster == null) return;
            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return;
            HashSet<IntVec3> attackerInterior = attacker.Interior(map);
            int attackerEff = gc.GetEffectiveMastery(attacker.caster, attacker.Tree);

            for (int i = domains.Count - 1; i >= 0; i--)
            {
                ActiveDomain sd = domains[i];
                DomainExtension p = sd.Props;
                if (p == null || !p.isSimpleDomain || sd.caster == null) continue;
                if (!sd.caster.HostileTo(attacker.caster)) continue;
                if (!Overlaps(sd.Interior(map), attackerInterior)) continue;

                // Wear only when the attacker out-masters the defender; the bigger
                // the gap, the faster it crumbles. Defender >= attacker => holds.
                int defenderEff = Mathf.Max(1, gc.GetEffectiveMastery(sd.caster, sd.Tree));
                float overpower = Mathf.Max(0f, (float)attackerEff / defenderEff - 0.9f);
                if (overpower <= 0f) continue;

                sd.integrity -= attackerProps.sureHitStrain * overpower;
                FleckMaker.Static(sd.center, map, FleckDefOf.PsycastAreaEffect, 1.5f);
                if (sd.integrity <= 0f)
                {
                    if (sd.caster.Faction != null && sd.caster.Faction.IsPlayer)
                    {
                        Messages.Message("JJK_SimpleDomainShattered".Translate(sd.caster.LabelShortCap),
                            sd.caster, MessageTypeDefOf.NegativeEvent, false);
                    }
                    Collapse(sd, voluntary: false);
                }
            }
        }

        private static bool Overlaps(HashSet<IntVec3> a, HashSet<IntVec3> b)
        {
            // Iterate the smaller set for cheapness.
            HashSet<IntVec3> small = a.Count <= b.Count ? a : b;
            HashSet<IntVec3> large = small == a ? b : a;
            foreach (IntVec3 c in small)
                if (large.Contains(c)) return true;
            return false;
        }

        // ===================== Enemy AI domain usage =====================
        // Non-player sorcerers open their domain on their own in a real fight. The
        // game has no built-in "make a pawn cast an ability" hook, so we drive it
        // from a throttled combat scan that calls Open() directly (the same path
        // the dev action uses). Burnout + HasDomain provide the natural cooldown.
        private int aiNextScanTick;

        private void TickEnemyDomainAI()
        {
            if (RimKaisenMod.Settings != null && !RimKaisenMod.Settings.enemyAIDomains) return;
            if (Find.TickManager.TicksGame < aiNextScanTick) return;
            aiNextScanTick = Find.TickManager.TicksGame + 250; // ~every 4s

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            if (gc == null) return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Faction == null || p.Faction.IsPlayer) continue;
                if (p.Dead || p.Downed || !p.Spawned) continue;
                if (!JJK_GradeUtility.IsSorcerer(p)) continue;
                if (HasDomain(p)) continue;
                if (p.health?.hediffSet?.GetFirstHediffOfDef(JJK_DefOf.JJK_DomainBurnout) != null) continue;
                TryEnemyOpenDomain(p);
            }
        }

        private void TryEnemyOpenDomain(Pawn p)
        {
            if (TechniqueSealUtil.IsSealed(p) || DomainAmplificationUtil.Active(p)) return;
            AbilityDef domainDef = PickEligibleDomain(p);
            if (domainDef == null) return;
            DomainExtension ext = domainDef.GetModExtension<DomainExtension>();
            if (ext == null) return;

            AbilityResource res = p.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (res == null || res.MaxEnergy < ext.minMaxCE) return;

            AbilityTierDef tier = domainDef.abilityTiers?.FirstOrDefault();
            float cost = tier?.energyCost ?? 100f;
            if (res.energy < cost) return;

            // Only bother when there are live enemies it would actually trap.
            int hostiles = CountHostilesNear(p, ext.radius);
            if (hostiles < 1) return;

            // More foes (or being badly hurt) makes a domain more likely each scan.
            float hpFrac = p.health.summaryHealth.SummaryHealthPercent;
            float chance = Mathf.Clamp(0.15f + 0.08f * hostiles + (hpFrac < 0.45f ? 0.30f : 0f), 0f, 0.85f);
            if (!Rand.Chance(chance)) return;

            res.energy -= cost;
            Open(p, domainDef);
        }

        // The strongest domain this pawn is actually entitled to: a cursed technique
        // it has unlocked whose domain tier its grade meets (minLevel <= grade).
        private AbilityDef PickEligibleDomain(Pawn pawn)
        {
            AbilityClass ac = pawn.GetComp<CompAbilities>()?.GetAbilityClass(JJK_DefOf.JJK_CursedTechniques);
            if (ac?.UnlockedTrees == null) return null;
            int grade = (int)(GameComponent_JJK.Instance?.GetGrade(pawn) ?? SorcererGrade.Grade4);

            foreach (AbilityDef def in DefDatabase<AbilityDef>.AllDefsListForReading)
            {
                DomainExtension ext = def.GetModExtension<DomainExtension>();
                if (ext == null || ext.isSimpleDomain) continue;
                AbilityTreeDef tree = def.abilityTrees?.FirstOrDefault();
                if (tree == null || !ac.UnlockedTrees.Contains(tree)) continue;
                AbilityTierDef tier = def.abilityTiers?.FirstOrDefault();
                int minLevel = tier?.acquireRequirement?.minLevel ?? 0;
                if (minLevel > grade) continue;
                return def;
            }
            return null;
        }

        private int CountHostilesNear(Pawn p, float radius)
        {
            int count = 0;
            float r2 = radius * radius;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == p || other.Dead || other.Downed || !other.Spawned) continue;
                if (!other.HostileTo(p)) continue;
                if ((other.Position - p.Position).LengthHorizontalSquared <= r2) count++;
            }
            return count;
        }

        // Domains are deliberately weak in untrained hands and grow with the
        // caster's mastery of the technique. This multiplier scales the sure-hit's
        // damage, stun and summon count: well below 1 for a novice, rising past 1
        // as effective mastery (grade + accumulated XP) climbs toward and beyond
        // MasteryRef. Practising a technique makes its domain hit harder.
        private const float MasteryRef = 2000f;
        private const float MasteryFactorMin = 0.4f;
        private const float MasteryFactorMax = 2.5f;

        private static float MasteryFactor(ActiveDomain d)
        {
            int mastery = GameComponent_JJK.Instance?.GetEffectiveMastery(d.caster, d.Tree) ?? 0;
            return Mathf.Clamp(mastery / MasteryRef, MasteryFactorMin, MasteryFactorMax) * d.RefinementPower;
        }

        private void Pulse(ActiveDomain d, DomainExtension props)
        {
            Pawn caster = d.caster;
            using var exposure = new MahoragaExposure(MahoragaPhenomena.ForAbility(d.sourceAbility), caster);

            FleckCreationData flash = FleckMaker.GetDataStatic(
                d.center.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, d.Radius * 1.5f);
            flash.instanceColor = props.barrierColor;
            map.flecks.CreateFleck(flash);

            bool doDamage = props.pulseDamage != null && props.pulseAmount > 0f;
            if (!doDamage && !props.pulseStun) return;

            float mastery = MasteryFactor(d);
            float output = Mathf.Max(0.5f, caster.GetStatValue(JJK_DefOf.JJK_CTOutput));
            float dmg = props.pulseAmount * output * mastery;
            int stunTicks = Mathf.RoundToInt(props.pulseStunTicks * mastery);

            foreach (Pawn p in HostilesInside(d))
            {
                // Time Cell Moon Palace: only what MOVES inside the domain is struck.
                if (props.pulseOnlyMoving && !(p.pather?.MovingNow ?? false)) continue;

                // Checked here rather than in HostilesInside because it is consumed:
                // the parry spends itself turning this one pulse aside.
                if (TryConsumeFallingBlossom(p)) continue;

                // Warded gear (e.g. the cursed helmet) blunts the sure-hit a little.
                float resist = Mathf.Clamp01(p.GetStatValue(JJK_DefOf.JJK_DomainSureHitResist));

                ApplyPulse(p, caster, props, doDamage, dmg, stunTicks, resist);
            }
        }

        private void ApplyPulse(Pawn p, Pawn caster, DomainExtension props, bool doDamage, float damage, int stunTicks, float resist)
        {
            if (props.pulseStun)
                p.stances?.stunner?.StunFor(Mathf.RoundToInt(stunTicks * (1f - resist)), caster);
            if (!doDamage) return;
            var dinfo = new DamageInfo(props.pulseDamage, damage * (1f - resist), 0f, -1f, caster);
            if (props.pulseIgnoresArmor) dinfo.SetIgnoreArmor(true);
            p.TakeDamage(dinfo);
        }

        // Show the actual flood-filled geometry, including closed domains. Never
        // draw another map's domains or expose cells hidden by fog.
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays || domains.Count == 0) return;
            for (int i = 0; i < domains.Count; i++)
            {
                ActiveDomain d = domains[i];
                DomainExtension props = d?.Props;
                if (props == null) continue;
                DomainInteriors.Draw(d, map);
            }
        }

        public override void MapComponentOnGUI()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays || Find.CameraDriver.CurrentZoom == CameraZoomRange.Furthest) return;
            foreach (var d in domains)
                if (d?.Props != null)
                    CombatPresentation.Label(map, d.center, CombatPresentation.DomainLabel(d), Color.white, -25);
        }

        public override void MapRemoved()
        {
            foreach (var d in domains) DomainInteriors.Release(d);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref domains, "domains", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (domains == null) domains = new List<ActiveDomain>();
                domains.RemoveAll(d => d == null || d.caster == null || d.sourceAbility == null);
            }
        }
    }
}
