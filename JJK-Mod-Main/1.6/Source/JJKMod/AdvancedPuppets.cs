using System;
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
    public class CompProperties_ThreeCores : CompProperties
    {
        public int maturationTicks = 2700000; // Three RimWorld quadrums; a deliberate time adaptation.
        public CompProperties_ThreeCores() { compClass = typeof(CompThreeCores); }
    }
    public enum PandaForm { Panda, Gorilla, Sister }
    public class CompThreeCores : ThingComp
    {
        public int maturesAt = -1, lostCores, nextSwitch;
        public PandaForm form;
        public bool announced;
        public Pawn Unit => (Pawn)parent;
        public CompCursedCorpse Core => Unit.TryGetComp<CompCursedCorpse>();
        public bool Mature => maturesAt >= 0 && Find.TickManager.TicksGame >= maturesAt;
        public static bool IsSoulCore(BodyPartRecord part) => part?.def?.defName?.StartsWith("JJK_SoulCore_") == true;
        public BodyPartRecord Part(PandaForm f) => Unit.RaceProps.body.AllParts.First(p => p.def.defName == "JJK_SoulCore_" + f);
        public bool Available(PandaForm f) => (lostCores & (1 << (int)f)) == 0 && !Unit.health.hediffSet.PartIsMissing(Part(f))
            && Unit.health.hediffSet.GetPartHealth(Part(f)) >= Part(f).def.hitPoints * Unit.HealthScale * 0.25f;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (maturesAt < 0) maturesAt = Find.TickManager.TicksGame + ((CompProperties_ThreeCores)props).maturationTicks;
            ApplyForm();
        }
        public void UpdateCores()
        {
            foreach (PandaForm f in Enum.GetValues(typeof(PandaForm)))
                if (Unit.health.hediffSet.PartIsMissing(Part(f))) lostCores |= 1 << (int)f;
            if (lostCores == 7) { if (!Unit.Dead) Unit.Kill(null); return; }
            if (!Available(form))
            {
                var usable = Enum.GetValues(typeof(PandaForm)).Cast<PandaForm>().Where(Available).ToList();
                if (usable.Count > 0) { form = usable[0]; ApplyForm(); }
                else Core.standby = true;
            }
        }
        public bool Switch(PandaForm requested)
        {
            UpdateCores();
            if (Unit.Dead || !Available(requested) || Find.TickManager.TicksGame < nextSwitch || requested == form || Core.charge < 5) return false;
            Core.charge -= 5; form = requested; nextSwitch = Find.TickManager.TicksGame + 180; ApplyForm(); return true;
        }
        public void ApplyForm()
        {
            foreach (var h in Unit.health.hediffSet.hediffs.Where(h => h.def.defName.StartsWith("JJK_PandaForm_")).ToList()) Unit.health.RemoveHediff(h);
            Unit.health.AddHediff(DefDatabase<HediffDef>.GetNamed("JJK_PandaForm_" + form));
            Unit.Drawer?.renderer?.SetAllGraphicsDirty();
        }
        public override void CompTick()
        {
            if (!Unit.Spawned || Unit.Dead || !Unit.IsHashIntervalTick(250)) return;
            UpdateCores();
            if (Mature)
            {
                Core.charge = Mathf.Min(120, Core.charge + 2);
                if (!announced) { announced = true; if (Unit.Faction == Faction.OfPlayer) Messages.Message(Unit.LabelShortCap + " has developed self-sustaining cores.", Unit, MessageTypeDefOf.PositiveEvent); }
            }
            if (Core.Operational && form == PandaForm.Gorilla) Core.charge = Mathf.Max(0, Core.charge - 3);
            if (Core.charge < 5 && form != PandaForm.Panda && Available(PandaForm.Panda)) { form = PandaForm.Panda; ApplyForm(); }
        }
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt) { if (!Unit.Dead) UpdateCores(); }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref maturesAt, "maturesAt", -1); Scribe_Values.Look(ref lostCores, "lostCores");
            Scribe_Values.Look(ref form, "form"); Scribe_Values.Look(ref nextSwitch, "nextSwitch"); Scribe_Values.Look(ref announced, "announced");
        }
        public override string CompInspectStringExtra() => "Three cores: " + string.Join(", ", Enum.GetValues(typeof(PandaForm)).Cast<PandaForm>().Select(f => f + (Available(f) ? " ready" : " unavailable")))
            + "\nForm: " + form + (Mature ? "\nSelf-sustaining; no controller slot required." : "\nCore maturation: " + Mathf.Max(0, (maturesAt - Find.TickManager.TicksGame) / 60000f).ToString("F1") + " days");
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Unit.Faction != Faction.OfPlayer || Unit.Dead) yield break;
            foreach (PandaForm f in Enum.GetValues(typeof(PandaForm)))
            {
                PandaForm selected = f;
                var cmd = new Command_Action { defaultLabel = f + " core", defaultDesc = "Switch form for 5 core charge. Injuries persist; destroyed cores cannot be replaced. Sister form's defensive role is a gameplay adaptation, since its canon attacks are unknown.", icon = TexCommand.Draft, action = () => Switch(selected) };
                if (!Available(f) || form == f || Core.charge < 5 || Find.TickManager.TicksGame < nextSwitch) cmd.Disable("Core unavailable, already active, recharging, or insufficient energy.");
                yield return cmd;
            }
        }
    }
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), nameof(Verb_MeleeAttackDamage.ApplyMeleeDamageToTarget))]
    public static class Patch_GorillaDrummingBeat
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target, DamageWorker.DamageResult __result)
        {
            var c = __instance.CasterPawn?.TryGetComp<CompThreeCores>();
            if (c?.form != PandaForm.Gorilla || !c.Core.Operational || c.Core.charge < 3 || __result?.totalDamageDealt <= 0 || __result == null || !(target.Thing is Pawn victim) || victim.Dead) return;
            c.Core.charge -= 3;
            var damage = new DamageInfo(DamageDefOf.Blunt, 12, 0, -1, c.Unit); damage.SetIgnoreArmor(true); victim.TakeDamage(damage);
        }
    }
    [HarmonyPatch(typeof(PawnRenderNode_AnimalPart), nameof(PawnRenderNode_AnimalPart.GraphicFor))]
    public static class Patch_PandaFormGraphic
    {
        public static void Postfix(Pawn pawn, ref Graphic __result)
        {
            var shell = pawn.TryGetComp<CompAnimalCorpseAppearance>();
            if (shell != null)
            {
                __result = AnimalCorpseAppearance.GraphicFor(shell.Animal);
                return;
            }
            var c = pawn.TryGetComp<CompThreeCores>();
            if (c == null) return;
            __result = AnimalCorpseAppearance.GraphicFor(AnimalCorpseAppearance.FormKind(c.form));
        }
    }

    public class CompProperties_CommunicationPuppet : CompProperties
    {
        public CompProperties_CommunicationPuppet() { compClass = typeof(CompCommunicationPuppet); }
    }
    public class CompCommunicationPuppet : ThingComp
    {
        public string message = "Watch the curse users. Keep the others safe.";
        public bool delivered;
        public int nextWarning;
        public Pawn Unit => (Pawn)parent;
        public CompCursedCorpse Core => Unit.TryGetComp<CompCursedCorpse>();
        public List<Pawn> Contacts() => !Core.Operational ? new List<Pawn>() : Unit.Map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && p.HostileTo(Unit)
            && Unit.Position.DistanceToSquared(p.Position) <= 900 && GenSight.LineOfSight(Unit.Position, p.Position, Unit.Map)).ToList();
        public override void CompTick()
        {
            if (!Unit.Spawned || Unit.Dead || !Unit.IsHashIntervalTick(250) || !Core.Operational || Unit.Faction != Faction.OfPlayer) return;
            if (!delivered && (Core.controller == null || Core.controller.Dead || Core.controller.Destroyed))
            { delivered = true; Find.LetterStack.ReceiveLetter("A last message", message, LetterDefOf.NeutralEvent, Unit); }
            var contacts = Contacts();
            if (contacts.Count > 0 && Find.TickManager.TicksGame >= nextWarning)
            { nextWarning = Find.TickManager.TicksGame + 5000; Messages.Message("Communication puppet: " + contacts.Count + " hostile contact(s) near " + Unit.Position + ".", contacts[0], MessageTypeDefOf.ThreatSmall); }
        }
        public override string CompInspectStringExtra() => "Communication relay; 30-tile line-of-sight warning radius.\n" + (delivered ? "Final message delivered." : "Final message stored.");
        public override void PostExposeData() { Scribe_Values.Look(ref message, "lastMessage", "Watch the curse users. Keep the others safe."); Scribe_Values.Look(ref delivered, "delivered"); Scribe_Values.Look(ref nextWarning, "nextWarning"); }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Unit.Faction != Faction.OfPlayer || Unit.Dead) yield break;
            yield return new Command_Action { defaultLabel = "Record message", defaultDesc = "Store a message to be delivered once if the creator dies while this relay still has charge.", icon = TexCommand.Draft, action = () => Find.WindowStack.Add(new Window_PuppetMessage(this)) };
            var view = new Command_Action { defaultLabel = "Locate contact", defaultDesc = "Jump to a hostile currently visible to this charged relay.", icon = TexCommand.Attack, action = () => { var p = Contacts().FirstOrDefault(); if (p != null) CameraJumper.TryJumpAndSelect(p); } };
            if (!Core.Operational) view.Disable("Relay has no power or is in standby."); yield return view;
        }
    }
    public class Window_PuppetMessage : Window
    {
        private readonly CompCommunicationPuppet relay;
        private string text;
        public override Vector2 InitialSize => new Vector2(520, 260);
        public Window_PuppetMessage(CompCommunicationPuppet relay) { this.relay = relay; text = relay.message; doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 35), "Message left by the puppet's creator");
            text = Widgets.TextArea(new Rect(0, 40, rect.width, 120), text);
            if (text.Length > 1000) text = text.Substring(0, 1000);
            if (Widgets.ButtonText(new Rect(0, 175, 140, 30), "Save message")) { relay.message = text; Close(); }
        }
    }

    public class PuppetSquadExtension : DefModExtension { public int count = 2; }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Patch_NPCPuppetRetreat
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance.Dead || __instance.Faction == Faction.OfPlayer || __instance.kindDef?.GetModExtension<PuppetSquadExtension>() == null) return;
            foreach (var p in GameComponent_Puppets.Instance.units.Where(p => p?.Spawned == true && p.TryGetComp<CompCursedCorpse>()?.controller == __instance).ToList()) p.Destroy();
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_NPCPuppetSquad
    {
        public static void Postfix(Pawn __instance, bool respawningAfterLoad) { if (!respawningAfterLoad) Provision(__instance); }
        public static bool Provision(Pawn owner)
        {
            var ext = owner?.kindDef?.GetModExtension<PuppetSquadExtension>(); var gc = GameComponent_Puppets.Instance;
            if (ext == null || gc == null || owner.Faction == null || owner.Faction == Faction.OfPlayer || !owner.Spawned || gc.provisionedOperators.Contains(owner)) return false;
            gc.provisionedOperators.Add(owner);
            if (!PuppetUtility.Manipulator(owner)) owner.genes.AddGene(PuppetUtility.Gene, true);
            owner.GetComp<CompAbilities>()?.RecheckAbilities();
            for (int i = 0; i < Math.Min(ext.count, PuppetUtility.Limit(owner)); i++)
            {
                var puppet = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("JJK_Mechamaru_Kind"), owner.Faction,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, fixedBiologicalAge: 0, fixedChronologicalAge: 0, fixedGender: Gender.None));
                var core = puppet.TryGetComp<CompCursedCorpse>(); core.controller = owner; core.charge = 120;
                GenSpawn.Spawn(puppet, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 3), owner.Map);
            }
            return true;
        }
    }
}
