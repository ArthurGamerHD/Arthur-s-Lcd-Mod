using LcdMod.Common.Config.Components;
using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Terminal.Controls.Scale;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.FarGridRaycastExperimentalApp))]
#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public sealed partial class FarGridRaycastExperimentalSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRenderScale>,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = MOD_PREFIX + "FarGridRaycastExperimental";
        public const string TITLE = "Far Grid Raycast Experimental";
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;


        public override IApp App => _app;
        FarGridRaycastExperimentalApp _app;

public override List<Control> InteractiveList => _app.VisualChildren as List<Control>;

        public FarGridRaycastExperimentalSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (_app == null)
                _app = new FarGridRaycastExperimentalApp(this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            CursorType = CursorType.Default;
            AddBackground(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
            DrawTitle(sprites);
            return sprites;
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (_app != null)
                _app.OnMouseScroll(delta, ref handled);
        }
    }
}
