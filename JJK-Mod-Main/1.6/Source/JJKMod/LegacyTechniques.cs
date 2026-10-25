using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;
using AbilityDef = TaranMagicFramework.AbilityDef;

namespace JJKMod
{
    public enum LegacyMove { LavaLance, EmberInsects, Meteor, Torrent, DeathSwarm, Roots, HugeRoots, CursedBuds, FlowerField, Convergence, PiercingBlood, Supernova, Cleave }

    public class LegacyMoveExtension : DefModExtension
    {
        public LegacyMove move;
        public float radius;
    }

    public static class LegacyTechniqueUtility
    {
        public static bool Operational(Pawn p) => p?.Spawned == true && !p.Dead && !p.Downed
            && !TechniqueSealUtil.IsSealed(p) && !DomainAmplificationUtil.Active(p)
            && !p.health.hediffSet.HasHediff(JJK_DefOf.JJK_DomainBurnout);

        // Secondary effects must not bypass Infinity even when no damage is dealt.
        public static bool Affected(Pawn caster, Pawn target) => target?.Spawned == true && !target.Dead
            && ExpansionTech.Hostile(caster, target) && !InfinityUtil.InfinityActive(target);

        public static List<Pawn> Targets(Pawn caster, Map map, IntVec3 center, float radius) =>
            GenRadial.RadialDistinctThingsAround(center, map, radius, true).OfType<Pawn>()
                .Where(p => Affected(caster, p) && GenSight.LineOfSight(center, p.Position, map)).ToList();

        public static float CleaveDamage(float output, float armor, float bodySize) =>
            (24f + Mathf.Clamp(armor * 12f + (bodySize - 1f) * 8f, 0f, 24f)) * output;

        public static List<IntVec3> LineCells(Pawn caster, IntVec3 end)
        {
            var cells = new List<IntVec3>();
            Vector3 direction = (end - caster.Position).ToVector3().normalized;
            IntVec3 previous = caster.Position;
            for (int i = 1; i <= Mathf.CeilToInt(caster.Position.DistanceTo(end)); i++)
            {
                var cell = (caster.Position.ToVector3Shifted() + direction * i).ToIntVec3();
                if (!cell.InBounds(caster.Map) || !GenSight.LineOfSight(previous, cell, caster.Map)
                    || cell.GetEdifice(caster.Map)?.def.Fillage == FillCategory.Full) break;
                if (cell != previous) cells.Add(cell);
                previous = cell;
            }
            return cells;
        }

        public static void Push(Pawn target, IntVec3 origin, int distance)
        {
            if (target?.Spawned != true || target.Dead) return;
            distance = MahoragaPhenomena.ControlTicks(target, distance);
            var map = target.Map;
            Vector3 direction = (target.Position - origin).ToVector3().normalized;
            IntVec3 destination = target.Position;
            for (int i = 1; i <= distance; i++)
            {
                IntVec3 next = (target.Position.ToVector3Shifted() + direction * i).ToIntVec3();
                if (!next.InBounds(map) || !next.Standable(map) || next.GetEdifice(map) != null
                    || next.GetFirstPawn(map) != null || !GenSight.LineOfSight(destination, next, map)) break;
                destination = next;
            }
            if (destination == target.Position) return;
            target.Position = destination;
            target.Notify_Teleported(false, true);
        }

        public static bool ConsumeConvergence(Pawn pawn)
        {
            var buff = pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("JJK_Convergence"));
            if (buff == null) return false;
            pawn.health.RemoveHediff(buff);
            return true;
        }
    }

    public class Ability_LegacyTechnique : Ability
    {
        public LegacyMoveExtension Props => def.GetModExtension<LegacyMoveExtension>();
        public override bool CanHitTarget(LocalTargetInfo target)
        {
            if (pawn?.Spawned != true || !target.IsValid || !target.Cell.InBounds(pawn.Map)) return false;
            if (target.HasThing && (!target.Thing.Spawned || target.Thing.Map != pawn.Map)) return false;
            if (Props.move == LegacyMove.Convergence) return target.Thing == pawn;
            float range = AbilityTier.effectRadius;
            if (!pawn.Position.InHorDistOf(target.Cell, range) || !GenSight.LineOfSight(pawn.Position, target.Cell, pawn.Map)) return false;
            if ((Props.move == LegacyMove.Cleave || Props.move == LegacyMove.CursedBuds)
                && !LegacyTechniqueUtility.Affected(pawn, target.Pawn)) return false;
            return base.CanHitTarget(target);
        }
        public override Func<string> CanBeActivatedValidator() => () =>
            LegacyTechniqueUtility.Operational(pawn) && CopyTechnique.Allowed(this) ? "" : "JJK_ExpansionUnavailable".Translate().ToString();

        public override void Start(bool consumeEnergy = true)
        {
            if (!LegacyTechniqueUtility.Operational(pawn) || !CopyTechnique.Allowed(this)) return;
            var move = Props.move;
            bool self = move == LegacyMove.Convergence;
            if (!self && (!curTarget.IsValid || !curTarget.Cell.InBounds(pawn.Map) || !CanHitTarget(curTarget)
                || !GenSight.LineOfSight(pawn.Position, curTarget.Cell, pawn.Map))) return;
            bool single = move == LegacyMove.Cleave || move == LegacyMove.CursedBuds;
            Pawn target = curTarget.Pawn;
            if (single && (target?.Map != pawn.Map || !LegacyTechniqueUtility.Affected(pawn, target))) return;
            Map map = pawn.Map;
            IntVec3 cell = self ? pawn.Position : curTarget.Cell;
            float output = ExpansionTech.Output(pawn);
            base.Start(consumeEnergy);
            // RegisterCast effects (including backlash from other systems) can down the caster.
            if (!LegacyTechniqueUtility.Operational(pawn)) return;
            var fields = map.GetComponent<MapComponent_LegacyTechniques>();
            switch (move)
            {
                case LegacyMove.LavaLance:
                case LegacyMove.PiercingBlood:
                    bool compressed = move == LegacyMove.PiercingBlood && LegacyTechniqueUtility.ConsumeConvergence(pawn);
                    Line(cell, (move == LegacyMove.LavaLance ? 28f : 34f) * output * (compressed ? 1.4f : 1f),
                        move == LegacyMove.LavaLance ? DamageDefOf.Burn : DamageDefOf.Cut, compressed ? 1.5f : .6f);
                    break;
                case LegacyMove.EmberInsects:
                    foreach (var p in LegacyTechniqueUtility.Targets(pawn, map, cell, Props.radius)) p.stances?.stunner?.StunFor(30, pawn);
                    MapComponent_DelayedBlasts.Queue(map, cell, 90, Props.radius, DamageDefOf.Flame, Mathf.RoundToInt(38 * output), pawn);
                    break;
                case LegacyMove.Meteor:
                    MapComponent_DelayedBlasts.Queue(map, cell, 240, Props.radius, DamageDefOf.Bomb, Mathf.RoundToInt(90 * output), pawn);
                    break;
                case LegacyMove.Torrent:
                    foreach (var p in LegacyTechniqueUtility.Targets(pawn, map, cell, Props.radius))
                    {
                        ExpansionTech.Hit(pawn, p, 24 * output, DamageDefOf.Blunt, .3f);
                        if (!p.Spawned || p.Dead) continue;
                        LegacyTechniqueUtility.Push(p, pawn.Position, 3);
                        ExpansionTech.Buff(p, "JJK_TidalDrag", 150);
                    }
                    FleckMaker.Static(cell, map, FleckDefOf.PsycastAreaEffect, Props.radius);
                    break;
                case LegacyMove.Roots: fields.Add(this, cell, 60, 1, 24 * output); break;
                case LegacyMove.HugeRoots: fields.Add(this, cell, 60, 6, 7 * output); break;
                case LegacyMove.DeathSwarm: fields.Add(this, cell, 45, 6, output); break;
                case LegacyMove.FlowerField: fields.Add(this, cell, 1, 8, 0); break;
                case LegacyMove.CursedBuds:
                    ExpansionTech.Buff(target, "JJK_CursedBuds", 900);
                    FleckMaker.Static(cell, map, FleckDefOf.PsycastAreaEffect, 1.2f);
                    break;
                case LegacyMove.Convergence: ExpansionTech.Buff(pawn, "JJK_Convergence", 900); break;
                case LegacyMove.Supernova:
                    bool prepared = LegacyTechniqueUtility.ConsumeConvergence(pawn);
                    MapComponent_DelayedBlasts.Queue(map, cell, 90, prepared ? 4f : Props.radius,
                        DamageDefOf.Cut, Mathf.RoundToInt((prepared ? 50 : 36) * output), pawn);
                    break;
                case LegacyMove.Cleave:
                    ExpansionTech.Hit(pawn, target, LegacyTechniqueUtility.CleaveDamage(output,
                        target.GetStatValue(StatDefOf.ArmorRating_Sharp), target.BodySize), DamageDefOf.Cut, 1.5f);
                    if (target.Spawned) FleckMaker.ThrowMicroSparks(target.DrawPos, map);
                    break;
            }
        }

        private void Line(IntVec3 end, float damage, DamageDef damageDef, float penetration)
        {
            var hit = new HashSet<Pawn>();
            Map map = pawn.Map;
            foreach (var cell in LegacyTechniqueUtility.LineCells(pawn, end))
            {
                foreach (Pawn p in cell.GetThingList(map).OfType<Pawn>().ToList())
                    if (LegacyTechniqueUtility.Affected(pawn, p) && hit.Add(p)) ExpansionTech.Hit(pawn, p, damage, damageDef, penetration);
                FleckMaker.Static(cell, map, FleckDefOf.PsycastAreaEffect, .35f);
            }
        }
    }

    public class LegacyField : IExposable
    {
        public Pawn caster;
        public AbilityDef ability;
        public IntVec3 cell;
        public int nextTick, pulses;
        public float power;
        public void ExposeData()
        {
            Scribe_References.Look(ref caster, "caster");
            Scribe_Defs.Look(ref ability, "ability");
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref nextTick, "nextTick");
            Scribe_Values.Look(ref pulses, "pulses");
            Scribe_Values.Look(ref power, "power");
        }
    }

    public class MapComponent_LegacyTechniques : MapComponent
    {
        public List<LegacyField> fields = new List<LegacyField>();
        public MapComponent_LegacyTechniques(Map map) : base(map) { }
        public void Add(Ability_LegacyTechnique ability, IntVec3 cell, int delay, int pulses, float power)
        {
            // Each caster can sustain only one field of each move. Recasting relocates it.
            fields.RemoveAll(f => f.caster == ability.pawn && f.ability == ability.def);
            fields.Add(new LegacyField { caster = ability.pawn, ability = ability.def, cell = cell,
                nextTick = ExpansionTech.Now + delay, pulses = pulses, power = power });
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref fields, "legacyFields", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (fields == null) fields = new List<LegacyField>();
                fields.RemoveAll(f => f?.caster == null || f.ability?.GetModExtension<LegacyMoveExtension>() == null || !f.cell.InBounds(map) || f.pulses <= 0);
            }
        }
        public override void MapComponentTick()
        {
            if (fields.Count == 0) return;
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                var f = fields[i];
                if (!LegacyTechniqueUtility.Operational(f.caster) || f.caster.Map != map
                    || JujutsuTraining.Class(f.caster)?.LearnedAbilities.Any(a => a.def == f.ability) != true)
                { fields.RemoveAt(i); continue; }
                if (ExpansionTech.Now < f.nextTick) continue;
                var ext = f.ability.GetModExtension<LegacyMoveExtension>();
                using (new MahoragaExposure(MahoragaPhenomena.ForAbility(f.ability), f.caster)) Pulse(f, ext);
                if (--f.pulses <= 0) fields.RemoveAt(i);
                else f.nextTick = ExpansionTech.Now + 60;
            }
        }
        private void Pulse(LegacyField f, LegacyMoveExtension ext)
        {
            var targets = LegacyTechniqueUtility.Targets(f.caster, map, f.cell, ext.radius);
            if (ext.move == LegacyMove.DeathSwarm)
            {
                // A fixed zone launches visible, interceptable fish. Only the domain has sure-hit.
                foreach (var p in targets.OrderBy(p => p.Position.DistanceToSquared(f.cell)).ThenBy(p => p.thingIDNumber).Take(3))
                {
                    var fish = (Projectile_LegacyFish)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_Proj_SwarmFish"), f.cell, map);
                    fish.output = f.power;
                    fish.Launch(f.caster, p.Position, p, ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld);
                }
            }
            else foreach (var p in targets)
            {
                if (ext.move == LegacyMove.FlowerField) ExpansionTech.Buff(p, "JJK_FlowerDistraction", 90);
                else
                {
                    ExpansionTech.Hit(f.caster, p, f.power, DamageDefOf.Stab, .35f);
                    if (p.Spawned && !p.Dead) ExpansionTech.Buff(p, "JJK_RootSnare", ext.move == LegacyMove.Roots ? 180 : 90);
                }
            }
            if (CombatPresentation.Visible(map, f.cell)) FleckMaker.Static(f.cell, map, FleckDefOf.PsycastAreaEffect, ext.radius);
        }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays) return;
            foreach (var f in fields)
            {
                var ext = f.ability.GetModExtension<LegacyMoveExtension>();
                var cells = CombatPresentation.VisibleCells(map, GenRadial.RadialCellsAround(f.cell, ext.radius, true));
                GenDraw.DrawFieldEdges(cells, ext.move == LegacyMove.DeathSwarm ? Color.cyan : new Color(.45f, .8f, .25f));
            }
        }
        public override void MapComponentOnGUI()
        {
            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                int row = 0;
                for (int j = 0; j < i; j++) if (fields[j].cell == f.cell) row++;
                CombatPresentation.Label(map, f.cell,
                    f.ability.LabelCap + " " + CombatPresentation.Countdown(f.nextTick + (f.pulses - 1) * 60), Color.white, -16 * row);
            }
        }
    }

    public class Projectile_LegacyFish : Bullet
    {
        public float output = 1f;
        public override int DamageAmount => Mathf.RoundToInt(base.DamageAmount * output);
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref output, "legacyOutput", 1f); }
    }

    public class Hediff_CursedBuds : HediffWithComps
    {
        private int nextFeed;
        public void Feed(float cost)
        {
            var energy = ExpansionTech.Energy(pawn);
            if (cost <= 0 || energy == null || ExpansionTech.Now < nextFeed || pawn.Dead) return;
            nextFeed = ExpansionTech.Now + 60;
            energy.energy = Mathf.Max(0, energy.energy - Mathf.Clamp(cost * .25f, 1f, 10f));
            Severity = Mathf.Min(3f, Severity + 1f);
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref nextFeed, "nextFeed"); }
    }

    [HarmonyPatch(typeof(Ability), "RegisterCast")]
    public static class Patch_CursedBudsFeed
    {
        public static void Postfix(Ability __instance, bool __runOriginal)
        {
            if (!__runOriginal || __instance.abilityResource?.def != JJK_DefOf.JJK_CursedEnergy || __instance.EnergyCost <= 0) return;
            __instance.pawn?.health?.hediffSet.hediffs.OfType<Hediff_CursedBuds>().FirstOrDefault()?.Feed(__instance.EnergyCost);
        }
    }
}
