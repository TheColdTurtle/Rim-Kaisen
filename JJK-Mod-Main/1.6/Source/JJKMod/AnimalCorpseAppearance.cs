using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Reuse each animal's authored adult graphic, including directional textures,
    // shader, tint and scale. Optional DLC species never become a hard dependency.
    public static class AnimalCorpseAppearance
    {
        public static PawnKindDef Kind(string name, string fallback = "Bear_Grizzly") =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(name) ?? DefDatabase<PawnKindDef>.GetNamed(fallback);
        public static Graphic GraphicFor(PawnKindDef kind) => kind.lifeStages.Last().bodyGraphicData.Graphic;
        public static PawnKindDef FormKind(PandaForm form) => form == PandaForm.Gorilla
            ? Kind("Gorilla", "Megasloth") : form == PandaForm.Sister ? Kind("Rhinoceros") : Kind("Panda");
    }
    public class CompProperties_AnimalCorpseAppearance : CompProperties
    {
        public CompProperties_AnimalCorpseAppearance() { compClass = typeof(CompAnimalCorpseAppearance); }
    }
    public class CompAnimalCorpseAppearance : ThingComp
    {
        private PawnKindDef animal;
        public PawnKindDef Animal => animal ?? AnimalCorpseAppearance.Kind("Bear_Grizzly");
        public static IEnumerable<PawnKindDef> Choices => new[] { "Bear_Grizzly", "Bear_Polar", "Fox_Red", "Fox_Arctic", "Fox_Fennec", "Panda" }
            .Select(n => DefDatabase<PawnKindDef>.GetNamedSilentFail(n)).Where(k => k != null);
        public bool Select(PawnKindDef kind)
        {
            var p = parent as Pawn;
            if (p == null || p.Dead || p.Faction != Faction.OfPlayer || !Choices.Contains(kind)) return false;
            animal = kind; p.Drawer?.renderer?.SetAllGraphicsDirty(); return true;
        }
        public override void PostExposeData() { base.PostExposeData(); Scribe_Defs.Look(ref animal, "shellAnimal"); }
        public override string CompInspectStringExtra() => "JJK_CorpseAnimalShell".Translate(Animal.LabelCap);
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Pawn p) || p.Dead || p.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "JJK_CorpseAppearance".Translate(), defaultDesc = "JJK_CorpseAppearanceDesc".Translate(),
                icon = AnimalCorpseAppearance.GraphicFor(Animal).MatSouth.mainTexture as Texture2D,
                action = () => Find.WindowStack.Add(new Window_CorpseAppearance(this))
            };
        }
    }
    public class Window_CorpseAppearance : Window
    {
        private readonly CompAnimalCorpseAppearance comp;
        public override Vector2 InitialSize => new Vector2(440, 440);
        public Window_CorpseAppearance(CompAnimalCorpseAppearance comp)
        { this.comp = comp; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 65), "JJK_CorpseAppearanceDesc".Translate());
            float y = 70;
            foreach (var kind in CompAnimalCorpseAppearance.Choices)
            {
                if (Widgets.ButtonText(new Rect(0, y, rect.width, 40), kind.LabelCap))
                { if (comp.Select(kind)) Close(); }
                y += 46;
            }
        }
    }
}
