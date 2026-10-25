using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Schedules explosions to go off after a delay, telegraphing the danger zone
    // with a precise warning boundary and countdown. Used by Ratio Technique:
    // Collapse, which gives the caster time to run before the blast lands.
    // Pending fuses are saved so reloading cannot cancel an armed Collapse.
    public class MapComponent_DelayedBlasts : MapComponent
    {
        public class Pending : IExposable
        {
            public IntVec3 cell;
            public int triggerTick;
            public float radius;
            public DamageDef damageDef;
            public int damageAmount;
            public Pawn instigator;
            public string phenomenon;

            public void ExposeData()
            {
                Scribe_Values.Look(ref cell, "cell");
                Scribe_Values.Look(ref triggerTick, "triggerTick");
                Scribe_Values.Look(ref radius, "radius");
                Scribe_Defs.Look(ref damageDef, "damageDef");
                Scribe_Values.Look(ref damageAmount, "damageAmount");
                Scribe_References.Look(ref instigator, "instigator");
                Scribe_Values.Look(ref phenomenon, "phenomenon");
            }
        }

        private List<Pending> pending = new List<Pending>();
        public int PendingCount => pending.Count;
        public IReadOnlyList<Pending> PendingBlasts => pending;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "pendingBlasts", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pending == null) pending = new List<Pending>();
                pending.RemoveAll(p => p == null || p.damageDef == null || !p.cell.InBounds(map));
            }
        }

        public MapComponent_DelayedBlasts(Map map) : base(map) { }

        public static void Queue(Map map, IntVec3 cell, int delayTicks, float radius,
            DamageDef damageDef, int damageAmount, Pawn instigator)
        {
            if (map == null) return;
            var comp = map.GetComponent<MapComponent_DelayedBlasts>();
            comp?.pending.Add(new Pending
            {
                cell = cell,
                triggerTick = Find.TickManager.TicksGame + Mathf.Max(1, delayTicks),
                radius = radius,
                damageDef = damageDef,
                damageAmount = damageAmount,
                instigator = instigator,
                phenomenon = MahoragaExposure.ForSource(instigator),
            });
            CombatPresentation.Play("Warning", map, cell);
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || !CombatPresentation.Overlays) return;
            foreach (var p in pending)
                CombatPresentation.Danger(map, GenRadial.RadialCellsAround(p.cell, p.radius, true).Where(c => c.InBounds(map)).ToList());
        }
        public override void MapComponentOnGUI()
        {
            foreach (var p in pending)
                CombatPresentation.Label(map, p.cell, "JJK_OverlayBlast".Translate(CombatPresentation.Countdown(p.triggerTick)), Color.white);
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];

                if (now >= p.triggerTick)
                {
                    if (p.cell.InBounds(map))
                    {
                        using (new MahoragaExposure(p.phenomenon, p.instigator))
                            GenExplosion.DoExplosion(p.cell, map, p.radius, p.damageDef, p.instigator, p.damageAmount);
                    }
                    pending.RemoveAt(i);
                }
            }
        }
    }
}
