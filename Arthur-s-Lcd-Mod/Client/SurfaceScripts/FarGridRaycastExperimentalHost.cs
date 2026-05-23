using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Client.Terminal.Controls.Scale;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public sealed class FarGridRaycastExperimentalSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRenderScale>,
        IUsesTerminalControl<SliderRaysPerTick>,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = "LcdMod_FarGridRaycastExperimental";
        public const string TITLE = "Far Grid Raycast Experimental";

        protected override ConfigKind ConfigKind => ConfigKind.Raycast;
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        readonly List<ControlBase> _interactiveListFallback = new List<ControlBase>();
        public override IApp App => _app;
        FarGridRaycastExperimentalApp _app;

        public override List<ControlBase> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;

        public FarGridRaycastExperimentalSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            var appConfig = Config as ScreenConfigRaycast;
            if (appConfig == null)
                return;

            if (_app == null)
                _app = new FarGridRaycastExperimentalApp(appConfig, this);

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
