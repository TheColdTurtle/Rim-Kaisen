using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Snapshot text and stable IDs survive dead/discarded pawns and pruned contracts.
    public class SorcererRecord : IExposable
    {
        public string pawnId, name;
        public int contracts, promotions, blackFlashes, shadowsLost, victories;
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId"); Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref contracts, "contracts"); Scribe_Values.Look(ref promotions, "promotions");
            Scribe_Values.Look(ref blackFlashes, "blackFlashes"); Scribe_Values.Look(ref shadowsLost, "shadowsLost");
            Scribe_Values.Look(ref victories, "victories");
        }
    }
    public class CampaignEntry : IExposable
    {
        public string pawnId, name, key, detail;
        public int tick;
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId"); Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref key, "key"); Scribe_Values.Look(ref detail, "detail"); Scribe_Values.Look(ref tick, "tick");
        }
        public string Text => "JJK_RecordEntry".Translate(tick / 60000 + 1, name, key.Translate(), detail);
    }
    public class GameComponent_CampaignHistory : GameComponent
    {
        public List<SorcererRecord> records = new List<SorcererRecord>();
        public List<CampaignEntry> entries = new List<CampaignEntry>();
        public static GameComponent_CampaignHistory Instance => Current.Game?.GetComponent<GameComponent_CampaignHistory>();
        public GameComponent_CampaignHistory(Game game) { }
        public SorcererRecord For(Pawn p)
        {
            var r = records.FirstOrDefault(x => x.pawnId == p.ThingID);
            if (r == null) { r = new SorcererRecord { pawnId = p.ThingID, name = p.LabelShortCap }; records.Add(r); }
            r.name = p.LabelShortCap; return r;
        }
        public void Add(Pawn p, string key, string detail = "")
        {
            if (p != null && p.Faction != Faction.OfPlayer) return;
            if (p != null)
            {
                var r = For(p);
                switch (key)
                {
                    case "JJK_RecordContract": r.contracts++; break;
                    case "JJK_RecordPromotion": r.promotions++; break;
                    case "JJK_RecordFlash": r.blackFlashes++; break;
                    case "JJK_RecordShadow": r.shadowsLost++; break;
                    case "JJK_RecordVictory": r.victories++; break;
                }
            }
            entries.Add(new CampaignEntry { pawnId = p?.ThingID, name = p?.LabelShortCap ?? "JJK_Headquarters".Translate().ToString(),
                key = key, detail = detail, tick = Find.TickManager.TicksGame });
            // Detailed chronology is bounded; lifetime totals are never truncated.
            if (entries.Count > 2000) entries.RemoveRange(0, entries.Count - 2000);
        }
        public void Contract(JujutsuContract c, bool success)
        {
            string key = success ? "JJK_RecordContract" : "JJK_RecordFailure";
            Add(null, key, c.Title);
            foreach (var p in c.participants.Where(p => p != null).Distinct()) Add(p, key, c.Title);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "sorcererRecords", LookMode.Deep);
            Scribe_Collections.Look(ref entries, "campaignHistory", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { records = records ?? new List<SorcererRecord>(); entries = entries ?? new List<CampaignEntry>(); }
        }
    }
    public class Window_CampaignHistory : Window
    {
        private readonly string pawnId;
        private Vector2 scroll;
        private float height = 600;
        public Window_CampaignHistory(Pawn pawn = null) { pawnId = pawn?.ThingID; doCloseX = true; absorbInputAroundWindow = true; }
        public override Vector2 InitialSize => new Vector2(820, 700);
        public override void DoWindowContents(Rect rect)
        {
            var gc = GameComponent_CampaignHistory.Instance;
            var view = new Rect(0, 0, rect.width - 24, Mathf.Max(height, rect.height));
            Widgets.BeginScrollView(rect, ref scroll, view);
            var list = new Listing_Standard(); list.Begin(view);
            list.Label("JJK_RecordTitle".Translate()); list.Label("JJK_RecordScope".Translate());
            foreach (var r in gc.records.Where(r => pawnId == null || r.pawnId == pawnId))
                list.Label("JJK_RecordTotals".Translate(r.name, r.contracts, r.promotions, r.blackFlashes, r.shadowsLost, r.victories));
            list.GapLine();
            foreach (var e in gc.entries.Where(e => pawnId == null || e.pawnId == pawnId).Reverse()) list.Label(e.Text);
            height = list.CurHeight + 30; list.End(); Widgets.EndScrollView();
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CampaignVictory
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out Pawn __state)
        {
            var killer = GameComponent_CullingGame.KillOwner(dinfo?.Instigator as Pawn);
            __state = !__instance.Dead && __instance.TryGetComp<CompBossCurse>() != null && killer != null
                && __instance.HostileTo(killer) ? killer : null;
        }
        public static void Postfix(Pawn __instance, Pawn __state)
        { if (__instance.Dead && __state != null) GameComponent_CampaignHistory.Instance?.Add(__state, "JJK_RecordVictory", __instance.LabelCap); }
    }
}
