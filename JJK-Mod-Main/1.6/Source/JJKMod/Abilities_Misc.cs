using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TaranMagicFramework;
using UnityEngine;
using Verse;
using Ability = TaranMagicFramework.Ability;

namespace JJKMod
{
    // Generic toggleable self-buff: while active, applies the hediff named in the
    // AbilityDef's ToggleHediffExtension. Lets new toggle techniques (Projection
    // Sorcery etc.) be data-driven instead of one C# class per buff.
    public class ToggleHediffExtension : DefModExtension
    {
        public HediffDef hediff;
    }

    public class Ability_ToggleHediff : Ability_Toggleable
    {
        private HediffDef Buff => def.GetModExtension<ToggleHediffExtension>()?.hediff;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            HediffDef buff = Buff;
            if (buff == null || pawn?.health == null) return;
            if (pawn.health.hediffSet.GetFirstHediffOfDef(buff) == null)
                pawn.health.AddHediff(buff);
        }

        public override void End()
        {
            base.End();
            HediffDef buff = Buff;
            Hediff h = buff == null ? null : pawn?.health?.hediffSet?.GetFirstHediffOfDef(buff);
            if (h != null) pawn.health.RemoveHediff(h);
        }
    }

    // Absorb Cursed Spirit: swallow a curse into the technique. Works on a curse
    // that is downed, or one at least two grades below the manipulator - anything
    // stronger resists. The absorbed curse joins the roster Release draws from.
    public class Ability_AbsorbCurse : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Pawn target = curTarget.Pawn;
            Map map = pawn?.Map;
            if (target == null || map == null || target.Dead) return;

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            AbsorbedCurseData data = gc?.GetAbsorbedCurses(pawn);
            if (data == null) return;

            if (!JJK_Utils.IsCursedSpirit(target))
            {
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_AbsorbNotCurse".Translate());
                return;
            }
            bool owned = data.deployed.Contains(target);
            if (!target.HostileTo(pawn) && !owned) return;
            if (data.Full)
            {
                MoteMaker.ThrowText(pawn.DrawPos, map, "JJK_AbsorbFull".Translate(AbsorbedCurseData.MaxStored));
                return;
            }

            // A curse can be swallowed while downed, or subjugated outright if it is
            // at least two grades beneath the manipulator.
            int userGrade = (int)gc.GetGrade(pawn);
            int curseGrade = (int)(target.def.GetModExtension<CursedSpiritExtension>()?.grade
                                   ?? SorcererGrade.Grade4);
            if (!owned && !target.Downed && userGrade - curseGrade < 2)
            {
                MoteMaker.ThrowText(target.DrawPos, map, "JJK_AbsorbResisted".Translate());
                return;
            }

            PawnKindDef kind = target.kindDef;
            FleckMaker.ThrowSmoke(target.DrawPos, map, 2f);
            if (!data.Capture(target)) return;
            Messages.Message("JJK_CurseAbsorbed".Translate(pawn.LabelShortCap, kind.LabelCap,
                    data.Count, AbsorbedCurseData.MaxStored),
                pawn, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    // Cursed Spirit Manipulation (Geto): release an absorbed curse to fight for
    // you. Releases the strongest curse in the manipulator's roster; with nothing
    // absorbed, falls back to a lesser curse from their standing stock (scaled by
    // grade). The released curse joins the caster's faction until slain.
    public class Ability_SummonCurse : Ability
    {
        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Map map = pawn?.Map;
            if (map == null) return;

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            AbsorbedCurseData roster = gc?.GetAbsorbedCurses(pawn);
            IntVec3 releaseCell = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 3);
            if (roster != null && roster.Count > 0)
            {
                roster.Release(releaseCell, map);
                return;
            }
            PawnKindDef kind = null;

            if (kind == null)
            {
                // Standing stock: a lesser curse scaled by grade (weaker than what a
                // dedicated absorber can field).
                int grade = (int)(gc?.GetGrade(pawn) ?? SorcererGrade.Grade4);
                float budget = 40f + grade * 60f;
                List<PawnKindDef> kinds = CursedSpiritUtil.CurseKinds()
                    .Where(k => k.combatPower <= budget).ToList();
                if (kinds.Count == 0) return;
                kind = kinds.OrderByDescending(k => k.combatPower).Take(2).RandomElement();
            }

            Pawn curse = PawnGenerator.GeneratePawn(kind, pawn.Faction);
            if (curse.Faction != pawn.Faction) curse.SetFaction(pawn.Faction, pawn);
            if (curse.training != null)
            {
                foreach (TrainableDef td in DefDatabase<TrainableDef>.AllDefsListForReading)
                {
                    if (curse.training.CanAssignToTrain(td, out _).Accepted)
                    {
                        curse.training.SetWantedRecursive(td, true);
                        curse.training.Train(td, pawn, complete: true);
                    }
                }
            }
            if (curse.playerSettings != null) curse.playerSettings.followDrafted = true;

            IntVec3 cell = CellFinder.RandomClosewalkCellNear(pawn.Position, map, 3);
            GenSpawn.Spawn(curse, cell, map);
            roster?.deployed.Add(curse);
            roster?.OrderDeployed();
            FleckMaker.ThrowSmoke(cell.ToVector3Shifted(), map, 2f);
        }
    }

    // Maximum: Uzumaki. Compress the roster selection into a single vortex
    // of cursed energy and unleash it. Consumes every stored curse; the blast's
    // damage and radius scale with how much combat power was fed in - a full
    // roster of strong curses is apocalyptic, a couple of grade 4s a firecracker.
    public class Ability_MaximumUzumaki : Ability
    {
        private const float BaseDamage = 25f;
        private const float DamagePerPower = 0.35f;
        private const float BaseRadius = 2.5f;

        public override void Start(bool consumeEnergy = true)
        {
            base.Start(consumeEnergy);
            Map map = pawn?.Map;
            if (map == null) return;

            GameComponent_JJK gc = GameComponent_JJK.Instance;
            AbsorbedCurseData data = gc?.GetAbsorbedCurses(pawn);
            if (data == null || data.SelectedCount == 0) return;

            IntVec3 center = curTarget.Cell;
            if (!center.InBounds(map)) return;

            float power = data.ConsumeSelected(out int count);
            float output = Mathf.Max(0.5f, pawn.GetStatValue(JJK_DefOf.JJK_CTOutput));
            int dmg = Mathf.RoundToInt((BaseDamage + power * DamagePerPower) * output);
            float radius = Mathf.Min(BaseRadius + count * 0.35f, 8f);

            GenExplosion.DoExplosion(center, map, radius, DamageDefOf.Bomb, pawn, dmg,
                armorPenetration: 1.5f);
            FleckMaker.Static(center, map, FleckDefOf.ExplosionFlash, radius * 1.5f);
            Messages.Message("JJK_UzumakiUnleashed".Translate(pawn.LabelShortCap, count, dmg),
                new LookTargets(center, map), MessageTypeDefOf.ThreatSmall, false);
        }

        public override System.Func<string> CanBeActivatedValidator()
        {
            return delegate
            {
                var data = GameComponent_JJK.Instance?.GetAbsorbedCurses(pawn);
                if (data == null || data.SelectedCount == 0)
                    return "JJK_UzumakiNoCurses".Translate();
                return "";
            };
        }
    }
}
