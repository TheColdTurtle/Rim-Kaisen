using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace JJKMod
{
    public partial class Comp_MahoragaAdaptation
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                defaultLabel = "JJK_MahoragaWheelDetails".Translate(),
                defaultDesc = "JJK_MahoragaWheelDetailsDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/CursedTechniques/Mahoraga", false),
                action = () => Find.WindowStack.Add(new Window_MahoragaAdaptation(this))
            };
        }
    }
    public class Window_MahoragaAdaptation : Window
    {
        private readonly Comp_MahoragaAdaptation wheel;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(720, 580);
        public Window_MahoragaAdaptation(Comp_MahoragaAdaptation wheel)
        {
            this.wheel = wheel; doCloseButton = true; doCloseX = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, rect.width, 35), "JJK_MahoragaWheelDetails".Translate()); Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, 42, rect.width, 68), "JJK_MahoragaWheelDetailsDesc".Translate());
            var viewport = new Rect(0, 115, rect.width, rect.height - 165);
            string text = wheel.AdaptationDescription();
            var content = new Rect(0, 0, viewport.width - 20, Mathf.Max(viewport.height, Text.CalcHeight(text, viewport.width - 20) + 12));
            Widgets.BeginScrollView(viewport, ref scroll, content); Widgets.Label(content, text); Widgets.EndScrollView();
        }
    }
}
