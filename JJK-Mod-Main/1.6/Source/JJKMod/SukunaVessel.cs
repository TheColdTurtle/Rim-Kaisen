using System.Linq;
using RimWorld;
using TaranMagicFramework;
using Verse;

namespace JJKMod
{
    public class HediffCompProperties_SukunaVessel : HediffCompProperties
    { public HediffCompProperties_SukunaVessel() { compClass = typeof(HediffComp_SukunaVessel); } }

    public class HediffComp_SukunaVessel : HediffComp
    {
        public int possessionUntil, loanUntil, suppressedUntil, nextSeizure, nextBargain;
        public bool debt, openedDomain;
        private Gene temporaryGene;
        public bool Possessed => possessionUntil > 0;
        public static HediffComp_SukunaVessel For(Pawn p) => p?.health?.hediffSet
            .GetFirstHediffOfDef(JJK_DefOf.JJK_SukunaInfluence)?.TryGetComp<HediffComp_SukunaVessel>();
        private static GeneDef Shrine => DefDatabase<GeneDef>.GetNamed("JJK_CT_Gene_Shrine");
        public override void CompExposeData()
        {
            Scribe_Values.Look(ref possessionUntil, "possessionUntil"); Scribe_Values.Look(ref loanUntil, "loanUntil");
            Scribe_Values.Look(ref suppressedUntil, "suppressedUntil"); Scribe_Values.Look(ref nextSeizure, "nextSeizure");
            Scribe_Values.Look(ref nextBargain, "nextBargain"); Scribe_Values.Look(ref debt, "vesselDebt");
            Scribe_Values.Look(ref openedDomain, "openedDomain"); Scribe_References.Look(ref temporaryGene, "temporaryShrineGene");
        }
        private void GrantShrine()
        {
            // Preserve an existing Shrine gene, including an inactive one owned by another source.
            if (Pawn.genes != null && !Pawn.genes.GenesListForReading.Any(g => g.def == Shrine))
                temporaryGene = Pawn.genes.AddGene(Shrine, true);
        }
        private void RemoveGift()
        {
            if (temporaryGene != null && Pawn.genes?.GenesListForReading.Contains(temporaryGene) == true)
                Pawn.genes.RemoveGene(temporaryGene);
            temporaryGene = null;
            foreach (string name in new[] { "JJK_SukunaLoan", "JJK_SukunaPossession" })
            {
                var h = Pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed(name));
                if (h != null) Pawn.health.RemoveHediff(h);
            }
            if (openedDomain) Pawn.MapHeld?.GetComponent<MapComponent_DomainManager>()?.CollapseFor(Pawn, false);
            openedDomain = false;
        }
        public bool CanBargain => Pawn.Spawned && !Pawn.Downed && !Pawn.Dead && !Pawn.InMentalState
            && !Possessed && loanUntil == 0 && !debt && Find.TickManager.TicksGame >= nextBargain
            && Find.TickManager.TicksGame >= suppressedUntil && Pawn.Faction == Faction.OfPlayer;
        public bool Bargain()
        {
            if (!CanBargain) return false;
            int now = Find.TickManager.TicksGame;
            parent.Severity = System.Math.Min(1f, parent.Severity + 0.1f);
            nextBargain = now + 60000; loanUntil = now + 6000;
            GrantShrine(); Pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_SukunaLoan"));
            var ce = Pawn.GetComp<CompAbilities>()?.GetAbilityResource(JJK_DefOf.JJK_CursedEnergy);
            if (ce != null) ce.energy = ce.MaxEnergy;
            Messages.Message("JJK_VesselBargainMade".Translate(), Pawn, MessageTypeDefOf.CautionInput);
            return true;
        }
        public bool BeginPossession()
        {
            if (!Pawn.Spawned || Pawn.Dead || Pawn.Downed || Pawn.InMentalState || Possessed
                || Find.TickManager.TicksGame < suppressedUntil) return false;
            if (!Pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, forced: true, forceWake: true)) return false;
            int now = Find.TickManager.TicksGame;
            debt = false; loanUntil = 0; possessionUntil = now + 1500 + (int)(parent.Severity * 1500);
            VesselDevelopment.Seized(Pawn);
            GrantShrine(); Pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_SukunaPossession"));
            if (parent.Severity >= 0.6f && Pawn.Map.mapPawns.AllPawnsSpawned.Count(p => p != Pawn && !p.Dead
                && p.Position.DistanceToSquared(Pawn.Position) < 225) >= 2)
            {
                var mgr = Pawn.Map.GetComponent<MapComponent_DomainManager>();
                if (!mgr.HasDomain(Pawn))
                { mgr.Open(Pawn, DefDatabase<TaranMagicFramework.AbilityDef>.GetNamed("JJK_Domain_Shrine")); openedDomain = mgr.HasDomain(Pawn); }
            }
            Find.LetterStack.ReceiveLetter("JJK_VesselSeized".Translate(), "JJK_VesselSeizedText".Translate(Pawn.LabelShortCap), LetterDefOf.ThreatBig, Pawn);
            return true;
        }
        public void EndPossession()
        {
            bool wasPossessed = Possessed;
            possessionUntil = 0; RemoveGift(); nextSeizure = Find.TickManager.TicksGame + 60000;
            if (wasPossessed && Pawn.MentalStateDef == MentalStateDefOf.Berserk)
                Pawn.mindState.mentalStateHandler.CurState.RecoverFromState();
        }
        public void Suppress()
        {
            suppressedUntil = Find.TickManager.TicksGame + 60000;
            if (loanUntil > 0) { loanUntil = 0; debt = true; }
            EndPossession();
        }
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.IsHashIntervalTick(60)) return;
            TickState();
        }
        public void TickState()
        {
            int now = Find.TickManager.TicksGame;
            if (Pawn.Dead) { EndPossession(); loanUntil = 0; return; }
            if (Possessed)
            {
                if (now >= possessionUntil || Pawn.Downed || Pawn.MentalStateDef != MentalStateDefOf.Berserk)
                { EndPossession(); return; }
                if (Pawn.Spawned && Pawn.IsHashIntervalTick(180))
                {
                    Pawn victim = Pawn.Map.mapPawns.AllPawnsSpawned.Where(p => p != Pawn && !p.Dead
                        && p.Position.DistanceToSquared(Pawn.Position) <= 400
                        && GenSight.LineOfSight(Pawn.Position, p.Position, Pawn.Map)).OrderBy(p => p.Position.DistanceToSquared(Pawn.Position)).FirstOrDefault();
                    if (victim != null)
                    {
                        var projectile = (Projectile)GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("JJK_Proj_Dismantle"), Pawn.Position, Pawn.Map);
                        projectile.Launch(Pawn, Pawn.DrawPos, victim, victim, ProjectileHitFlags.IntendedTarget);
                    }
                }
                return;
            }
            if (loanUntil > 0)
            {
                if (now < loanUntil) return;
                loanUntil = 0; debt = true; RemoveGift();
            }
            if (now < suppressedUntil) return;
            if (debt) { BeginPossession(); return; }
            if (parent.Severity < 0.25f || now < nextSeizure) return;
            nextSeizure = now + 15000;
            // Escalating random incidents, with at least a day between actual seizures.
            if (Rand.Chance(0.03f + parent.Severity * 0.12f)) BeginPossession();
        }
        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null) { EndPossession(); loanUntil = 0; }
        public override void CompPostPostRemoved() { EndPossession(); base.CompPostPostRemoved(); }
        public override string CompTipStringExtra => "JJK_VesselStatus".Translate(
            Possessed ? "JJK_VesselPossessed".Translate() : loanUntil > 0 ? "JJK_VesselLoan".Translate()
            : Find.TickManager.TicksGame < suppressedUntil ? "JJK_VesselContained".Translate()
            : debt ? "JJK_VesselDebt".Translate() : "JJK_VesselDormant".Translate());
    }
    public class CompProperties_ContainVessel : CompProperties_UseEffect
    { public CompProperties_ContainVessel() { compClass = typeof(CompUseEffect_ContainVessel); } }
    public class CompUseEffect_ContainVessel : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn p) => HediffComp_SukunaVessel.For(p) != null
            ? base.CanBeUsedBy(p) : new AcceptanceReport("JJK_NoVessel".Translate());
        public override void DoEffect(Pawn usedBy) { base.DoEffect(usedBy); HediffComp_SukunaVessel.For(usedBy)?.Suppress(); }
    }
}
