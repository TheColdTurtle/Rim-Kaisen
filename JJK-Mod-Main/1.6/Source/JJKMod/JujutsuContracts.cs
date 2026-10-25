using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public enum ContractKind { Infestation, Rescue, Recovery, Boss, Investigation, Evacuation, Haunting, Sealing }
    public enum ContractState { Active, Ready, Completed, Failed }
    public class SchoolStanding : IExposable
    {
        public Faction school;
        public int reputation, nextOfferTick;
        public int MaxTier => reputation >= 50 ? 3 : reputation >= 20 ? 2 : 1;
        public void ExposeData()
        {
            Scribe_References.Look(ref school, "school");
            Scribe_Values.Look(ref reputation, "reputation"); Scribe_Values.Look(ref nextOfferTick, "nextOfferTick");
        }
    }
    public class JujutsuContract : IExposable
    {
        public Faction school;
        public Map map;
        public ContractKind kind;
        public ContractState state;
        public int tier, expires;
        public List<Pawn> threats = new List<Pawn>();
        public Pawn rescue;
        public Thing artifact;
        public ThingDef rewardDef;
        public bool expedition, siteGenerated, siteCleared;
        public ExorcismSite site;
        public List<Pawn> participants = new List<Pawn>();
        public List<Pawn> supervisedStudents = new List<Pawn>();
        public List<Pawn> supervisedMentors = new List<Pawn>();
        public ColonyMissionData colony;
        public SchoolCampaignData campaign;
        public SealingData sealing;
        public bool promotionTrial;
        public Pawn trialCandidate;
        public int trialGrade, trialRequired;
        public List<string> trialEngaged = new List<string>();
        public string Title => (school?.def.LabelCap.ToString() ?? "?") + " — " + (campaign != null ? "JJK_SchoolCampaign".Translate() : promotionTrial ? "JJK_TrialTitle".Translate(trialCandidate?.LabelShortCap ?? "?") : ("JJK_Contract" + kind).Translate()) + " (" + tier + ")";
        public int Remaining => threats.Count(p => p != null && !p.Dead && !p.Destroyed && p.Faction != Faction.OfPlayer);
        public void ExposeData()
        {
            Scribe_Deep.Look(ref colony, "colonyMission");
            Scribe_Deep.Look(ref campaign, "schoolCampaign"); Scribe_Deep.Look(ref sealing, "sealingCampaign");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                for (int i = 0; i < supervisedStudents.Count; i++) if (supervisedStudents[i]?.Destroyed == true) supervisedStudents[i] = null;
                for (int i = 0; i < supervisedMentors.Count; i++) if (supervisedMentors[i]?.Destroyed == true) supervisedMentors[i] = null;
            }
            Scribe_Collections.Look(ref supervisedStudents, "supervisedStudents", LookMode.Reference);
            Scribe_Collections.Look(ref supervisedMentors, "supervisedMentors", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && supervisedStudents == null) supervisedStudents = new List<Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && supervisedMentors == null) supervisedMentors = new List<Pawn>();
            Scribe_References.Look(ref school, "school"); Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref tier, "tier"); Scribe_Values.Look(ref expires, "expires");
            Scribe_Defs.Look(ref rewardDef, "rewardDef");
            Scribe_Values.Look(ref promotionTrial, "promotionTrial"); Scribe_References.Look(ref trialCandidate, "trialCandidate");
            Scribe_Values.Look(ref trialGrade, "trialGrade"); Scribe_Values.Look(ref trialRequired, "trialRequired");
            Scribe_Collections.Look(ref trialEngaged, "trialEngaged", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && trialEngaged == null) trialEngaged = new List<string>();
            Scribe_Values.Look(ref expedition, "expedition"); Scribe_Values.Look(ref siteGenerated, "siteGenerated");
            Scribe_Values.Look(ref siteCleared, "siteCleared"); Scribe_References.Look(ref site, "expeditionSite");
            if (Scribe.mode == LoadSaveMode.Saving) participants.RemoveAll(p => p == null || p.Destroyed);
            Scribe_Collections.Look(ref participants, "expeditionParticipants", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.Saving) threats.RemoveAll(p => p == null || p.Destroyed);
            Scribe_Collections.Look(ref threats, "threats", LookMode.Reference);
            Scribe_References.Look(ref rescue, "rescue"); Scribe_References.Look(ref artifact, "artifact");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && threats == null) threats = new List<Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && participants == null) participants = new List<Pawn>();
        }
        public bool ObjectiveMet()
        {
            if (expedition) return ExorcismExpeditions.ObjectiveMet(this);
            if (map == null || Remaining > 0) return false;
            if (promotionTrial) return PromotionTrials.Valid(this) && trialEngaged.Count >= trialRequired;
            if (kind == ContractKind.Rescue)
                return rescue != null && !rescue.Dead && rescue.Spawned && !rescue.Downed
                    && rescue.Map == map && map.areaManager.Home[rescue.Position];
            if (kind == ContractKind.Recovery)
                return artifact != null && !artifact.Destroyed && artifact.Spawned && artifact.Map == map
                    && map.areaManager.Home[artifact.Position];
            return true;
        }
    }

    public class GameComponent_Jujutsu : GameComponent
    {
        public List<SchoolStanding> standings = new List<SchoolStanding>();
        public List<JujutsuContract> contracts = new List<JujutsuContract>();
        public List<TrainingPlan> training = new List<TrainingPlan>();
        public GameComponent_Jujutsu(Game game) { }
        public static GameComponent_Jujutsu Instance => Current.Game?.GetComponent<GameComponent_Jujutsu>();
        public TrainingPlan Plan(Pawn p)
        {
            var plan = training.FirstOrDefault(x => x.pupil == p);
            if (plan == null) { plan = new TrainingPlan { pupil = p }; training.Add(plan); }
            return plan;
        }
        public SchoolStanding Standing(Faction f)
        {
            var standing = standings.FirstOrDefault(s => s.school == f);
            if (standing == null) { standing = new SchoolStanding { school = f }; standings.Add(standing); }
            return standing;
        }
        public static IEnumerable<Faction> Schools => Find.FactionManager.AllFactionsListForReading
            .Where(f => !f.defeated && (f.def.defName == "JJK_TokyoHigh" || f.def.defName == "JJK_KyotoHigh"));
        public ThingDef RewardFor(Faction school, int tier)
        {
            string[] rewards = tier <= 1 ? new[] { "JJK_CursedResidue" }
                : tier == 2 ? new[] { "JJK_SimpleDomainScroll", "JJK_NewShadowStyleScroll", "JJK_FallingBlossomEmotionScroll", "JJK_DomainAmplificationScroll" }
                : new[] { "JJK_NanamiBluntSword", "JJK_GradeTrainingManual", "JJK_TechniqueMasteryManual", "JJK_NobaraHammer" };
            int index = (Standing(school).reputation / 10 + (school.def.defName == "JJK_KyotoHigh" ? 1 : 0)) % rewards.Length;
            return DefDatabase<ThingDef>.GetNamed(rewards[index]);
        }
        public bool CanAccept(Faction school, Map map, int tier, ContractKind kind)
            => school != null && Schools.Contains(school) && !school.HostileTo(Faction.OfPlayer)
                && map != null && map.IsPlayerHome && tier >= 1 && tier <= Standing(school).MaxTier
                && (kind != ContractKind.Boss || tier == 3)
                && Standing(school).nextOfferTick <= Find.TickManager.TicksGame
                && !contracts.Any(c => c.school == school && (c.state == ContractState.Active || c.state == ContractState.Ready));
        public JujutsuContract Accept(Faction school, Map map, int tier, ContractKind kind, Pawn trialCandidate = null)
        {
            if (ColonyMissions.Special(kind) || kind == ContractKind.Sealing) return null; // These assignments require a separate expedition map.
            if (trialCandidate != null && (trialCandidate.Map != map || kind != ContractKind.Infestation
                || tier != PromotionTrials.Tier(trialCandidate) || PromotionTrials.Reason(school, trialCandidate) != null)) return null;
            if (!CanAccept(school, map, tier, kind)
                || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Animal)) return null;
            var c = new JujutsuContract { school = school, map = map, tier = tier, kind = kind,
                expires = Find.TickManager.TicksGame + 180000, rewardDef = RewardFor(school, tier) };
            if (trialCandidate != null)
            {
                c.promotionTrial = true; c.trialCandidate = trialCandidate; c.trialGrade = (int)GameComponent_JJK.Instance.GetGrade(trialCandidate);
                c.rewardDef = PromotionTrials.Reward(c.trialGrade);
            }
            if (kind == ContractKind.Boss)
            {
                string[] bosses = { "JJK_Boss_Jogo_Kind", "JJK_Boss_Hanami_Kind", "JJK_Boss_Dagon_Kind", "JJK_Boss_Mahito_Kind" };
                var boss = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(bosses.RandomElement()));
                GenSpawn.Spawn(boss, entry, map); c.threats.Add(boss);
            }
            c.threats.AddRange(CursedSpiritUtil.SpawnPack(map, entry, c.promotionTrial ? PromotionTrials.Points(c.trialGrade) : 130f * tier, c.promotionTrial ? 12 : 8));
            if (c.threats.Count == 0) return null;
            c.trialRequired = Mathf.Min(2, c.threats.Count);
            if (kind == ContractKind.Rescue)
            {
                c.rescue = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_SorcererApprentice"),
                    school, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                c.rescue.health.AddHediff(HediffDefOf.Anesthetic);
                GenSpawn.Spawn(c.rescue, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            }
            if (kind == ContractKind.Recovery)
            {
                c.artifact = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("JJK_CursedEnergyTalisman"));
                GenPlace.TryPlaceThing(c.artifact, entry, map, ThingPlaceMode.Near);
                c.artifact.SetForbidden(false);
            }
            contracts.Add(c);
            Find.LetterStack.ReceiveLetter(c.Title, "JJK_ContractAccepted".Translate(), LetterDefOf.ThreatBig, new LookTargets(entry, map));
            return c;
        }
        public void Evaluate(JujutsuContract c)
        {
            if (c.state != ContractState.Active && c.state != ContractState.Ready) return;
            if (c.promotionTrial && !PromotionTrials.Valid(c)) { Fail(c); return; }
            if (c.expedition) { ExorcismExpeditions.Evaluate(c); return; }
            if (c.map == null || !Find.Maps.Contains(c.map) || c.school == null
                || c.school.HostileTo(Faction.OfPlayer) || Find.TickManager.TicksGame >= c.expires
                || (c.kind == ContractKind.Rescue && (c.rescue == null || c.rescue.Dead || c.rescue.Destroyed))
                || (c.kind == ContractKind.Recovery && (c.artifact == null || c.artifact.Destroyed)))
            { Fail(c); return; }
            bool ready = c.ObjectiveMet();
            if (ready && c.state != ContractState.Ready)
                Messages.Message("JJK_ContractReady".Translate(c.Title), MessageTypeDefOf.PositiveEvent);
            c.state = ready ? ContractState.Ready : ContractState.Active;
        }
        public bool Claim(JujutsuContract c)
        {
            Evaluate(c);
            if (c.state != ContractState.Ready || !c.ObjectiveMet()) return false;
            Thing reward = ThingMaker.MakeThing(c.rewardDef ?? RewardFor(c.school, c.tier));
            if (c.tier == 1 && !c.promotionTrial) reward.stackCount = 60;
            IntVec3 drop = c.map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? c.map.Center;
            if (!GenPlace.TryPlaceThing(reward, drop, c.map, ThingPlaceMode.Near)) { reward.Destroy(); return false; }
            c.state = ContractState.Completed;
            if (!c.expedition) c.participants.AddRange(c.map.mapPawns.FreeColonistsSpawned.Where(JJK_GradeUtility.IsSorcerer));
            GameComponent_CampaignHistory.Instance?.Contract(c, true);
            if (c.sealing?.vessel != null && !c.sealing.vessel.Destroyed) c.sealing.vessel.Destroy();
            GameComponent_ColonyLife.Instance?.CompleteMission(c);
            if (c.expedition && c.kind == ContractKind.Recovery && c.artifact != null && !c.artifact.Destroyed)
                c.artifact.Destroy();
            var s = Standing(c.school); s.reputation = Mathf.Min(100, s.reputation + 10 * c.tier);
            s.nextOfferTick = Find.TickManager.TicksGame + 120000;
            c.school.TryAffectGoodwillWith(Faction.OfPlayer, 5);
            if (c.promotionTrial) PromotionTrials.Complete(c);
            else foreach (Pawn p in (c.expedition ? c.participants.Where(p => p != null && !p.Dead && p.Faction == Faction.OfPlayer && p.MapHeld == c.map)
                : c.map.mapPawns.FreeColonistsSpawned.AsEnumerable()).Where(JJK_GradeUtility.IsSorcerer))
                GameComponent_JJK.Instance.AddGradeXp(p, 25 * c.tier);
            Messages.Message("JJK_ContractPaid".Translate(c.school.Name, reward.LabelCap), new LookTargets(drop, c.map), MessageTypeDefOf.PositiveEvent);
            return true;
        }
        public void Fail(JujutsuContract c)
        {
            if (c.state == ContractState.Completed || c.state == ContractState.Failed) return;
            c.state = ContractState.Failed;
            GameComponent_CampaignHistory.Instance?.Contract(c, false);
            if (c.promotionTrial && c.trialCandidate != null)
                GameComponent_SorcererStudy.Instance.For(c.trialCandidate).nextTrial = Find.TickManager.TicksGame + 300000;
            if (c.school != null)
            {
                var s = Standing(c.school); s.reputation = Mathf.Max(0, s.reputation - 5);
                s.nextOfferTick = Find.TickManager.TicksGame + 120000;
            }
            Messages.Message("JJK_ContractFailed".Translate(), MessageTypeDefOf.NegativeEvent);
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0) return;
            foreach (var c in contracts) Evaluate(c);
            foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<ExorcismSite>().ToList())
                if (!site.HasMap && !site.ActiveContract) site.Destroy();
        }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                training.RemoveAll(t => t.pupil == null || t.pupil.Destroyed);
                foreach (var t in training) if (t.mentor?.Destroyed == true) t.mentor = null;
                // Retain a small history without serializing destroyed mission subjects.
                contracts.RemoveAll(c => c.state == ContractState.Completed || c.state == ContractState.Failed);
            }
            Scribe_Collections.Look(ref standings, "schoolStandings", LookMode.Deep);
            Scribe_Collections.Look(ref contracts, "schoolContracts", LookMode.Deep);
            Scribe_Collections.Look(ref training, "trainingPlans", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (standings == null) standings = new List<SchoolStanding>();
                if (contracts == null) contracts = new List<JujutsuContract>();
                if (training == null) training = new List<TrainingPlan>();
                standings.RemoveAll(s => s.school == null); training.RemoveAll(t => t.pupil == null);
            }
        }
    }
}
